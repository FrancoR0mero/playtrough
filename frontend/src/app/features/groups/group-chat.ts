import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  Injector,
  input,
  OnDestroy,
  OnInit,
  output,
  signal,
  viewChild,
  ElementRef,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { HubConnection } from '@microsoft/signalr';
import { firstValueFrom, Subject, takeUntil } from 'rxjs';
import { normalizePlainText } from '../../shared/plain-text';
import { AuthService } from '../auth/services/auth';
import { ChatMessage, GroupChatConnection } from './group-chat-connection';
import { ChatHistoryQuery, GroupsApi } from './groups-api';

@Component({
  selector: 'app-group-chat',
  imports: [DatePipe, ReactiveFormsModule],
  templateUrl: './group-chat.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GroupChatComponent implements OnInit, OnDestroy {
  readonly groupId = input.required<number>();
  readonly accessLost = output<void>();
  private readonly client = inject(GroupChatConnection);
  private readonly api = inject(GroupsApi);
  private readonly auth = inject(AuthService);
  private readonly injector = inject(Injector);
  private readonly stopReads = new Subject<void>();
  private syncGeneration = 0;
  // Only the paginated HTTP response can confirm a continuous range of IDs.
  private checkpoint: string | null = null;
  private connection?: HubConnection;
  private draftAttempt?: { id: string; text: string };
  private destroyed = false;
  private readonly log = viewChild<ElementRef<HTMLElement>>('messageLog');

  protected readonly settings = { maxLength: 2000 };
  protected readonly draft = new FormControl('', { nonNullable: true });
  private readonly draftValue = toSignal(this.draft.valueChanges, { initialValue: '' });
  protected readonly clean = computed(() => normalizePlainText(this.draftValue()));
  protected readonly count = computed(() => Array.from(this.clean()).length);
  protected readonly rows = signal<ChatMessage[]>([]);
  protected readonly state = signal<
    'connecting' | 'ready' | 'reconnecting' | 'offline' | 'revoked'
  >('connecting');
  protected readonly error = signal('');
  protected readonly historyError = signal(false);
  protected readonly syncing = signal(false);
  protected readonly olderError = signal(false);
  protected readonly olderLoading = signal(false);
  protected readonly before = signal<string | null>(null);
  protected readonly sending = signal(false);
  protected readonly userId = computed(() => this.auth.currentUser()?.id);

  ngOnInit(): void {
    const connection = this.client.create();
    this.connection = connection;
    connection.on('MessageCreated', (message: ChatMessage) => this.receive(message));
    connection.on('ChatAccessRevoked', (groupId: number) => {
      if (groupId === this.groupId()) this.revoke();
    });
    connection.onreconnecting(() => {
      if (this.active()) this.state.set('reconnecting');
    });
    connection.onreconnected(() => this.subscribe(connection));
    connection.onclose(() => {
      if (this.active()) this.state.set('offline');
    });
    void this.connect();
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.stopReads.next();
    this.stopReads.complete();
    void this.connection?.stop();
  }

  protected async connect(): Promise<void> {
    const connection = this.connection;
    if (!connection || !this.active()) return;
    this.state.set('connecting');
    this.error.set('');
    try {
      await connection.start();
      await this.subscribe(connection);
    } catch {
      if (this.active()) this.state.set('offline');
    }
  }

  protected async send(): Promise<void> {
    const connection = this.connection;
    if (
      !connection ||
      this.state() !== 'ready' ||
      this.sending() ||
      this.count() < 1 ||
      this.count() > this.settings.maxLength
    )
      return;
    const text = this.clean();
    const attempt =
      this.draftAttempt?.text === text ? this.draftAttempt : { id: crypto.randomUUID(), text };
    this.draftAttempt = attempt;
    this.sending.set(true);
    this.error.set('');
    try {
      const message = await connection.invoke<ChatMessage>(
        'SendMessage',
        this.groupId(),
        attempt.id,
        text,
      );
      if (!this.active()) return;
      this.receive(message);
      this.draft.reset();
      this.draftAttempt = undefined;
    } catch (error) {
      if (!this.active()) return;
      if (this.hasCode(error, 'chat_not_available')) this.revoke();
      else if (this.hasCode(error, 'chat_rate_limited')) {
        this.error.set(
          'Enviaste muchos mensajes seguidos. Esperá unos segundos y volvé a intentar.',
        );
      } else if (this.hasCode(error, 'chat_invalid_message')) {
        this.error.set('Revisá que el mensaje tenga entre 1 y 2.000 caracteres.');
      } else {
        this.error.set('No se confirmó el envío. Podés volver a intentarlo.');
      }
    } finally {
      this.sending.set(false);
    }
  }

  private async subscribe(connection: HubConnection): Promise<void> {
    if (!this.active()) return;
    try {
      await connection.invoke('JoinGroup', this.groupId());
      if (!this.active()) return;
      this.state.set('ready');
      await this.syncHistory();
    } catch (error) {
      if (!this.active()) return;
      if (this.hasCode(error, 'chat_not_available')) this.revoke();
      else {
        await connection.stop();
        if (this.active()) this.state.set('offline');
      }
    }
  }

  protected async syncHistory(): Promise<void> {
    if (!this.active()) return;
    const generation = ++this.syncGeneration;
    const current = () => this.active() && this.syncGeneration === generation;
    this.syncing.set(true);
    this.historyError.set(false);
    try {
      if (this.checkpoint === null) {
        const page = await this.readHistory();
        if (!page || !current()) return;
        if (page.through === null) throw new Error('Missing chat checkpoint');
        this.merge(page.items);
        this.before.set(page.nextCursor);
        this.checkpoint = page.through;
        return;
      }
      let through: string | undefined;
      let after = this.checkpoint;
      for (;;) {
        const page = await this.readHistory({ after, through });
        if (!page || !current()) return;
        if (page.through === null || page.nextCursor === after)
          throw new Error('Invalid chat recovery page');
        this.merge(page.items);
        through = page.through;
        if (page.nextCursor === null) {
          this.checkpoint = through;
          return;
        }
        after = page.nextCursor;
        this.checkpoint = after;
      }
    } catch (error) {
      if (current()) {
        if (error instanceof HttpErrorResponse && error.status === 404) this.revoke();
        else this.historyError.set(true);
      }
    } finally {
      if (current()) this.syncing.set(false);
    }
  }

  protected async loadOlder(): Promise<void> {
    const before = this.before();
    if (!before || this.olderLoading() || !this.active()) return;
    this.olderLoading.set(true);
    this.olderError.set(false);
    try {
      const page = await this.readHistory({ before });
      if (!page || !this.active()) return;
      this.merge(page.items, true);
      this.before.set(page.nextCursor);
    } catch (error) {
      if (this.active()) {
        if (error instanceof HttpErrorResponse && error.status === 404) this.revoke();
        else this.olderError.set(true);
      }
    } finally {
      this.olderLoading.set(false);
    }
  }

  private readHistory(query?: ChatHistoryQuery) {
    return firstValueFrom(
      this.api.chatHistory(this.groupId(), query).pipe(takeUntil(this.stopReads)),
      { defaultValue: null },
    );
  }

  private receive(message: ChatMessage): void {
    if (this.active()) this.merge([message]);
  }

  private merge(messages: ChatMessage[], prepend = false): void {
    const stored = new Map(this.rows().map((item) => [item.id, item]));
    for (const message of messages)
      if (message.groupId === this.groupId() && !stored.has(message.id))
        stored.set(message.id, message);
    if (stored.size === this.rows().length) return;
    const log = this.log()?.nativeElement;
    const logTop = log?.getBoundingClientRect().top ?? 0;
    const anchor =
      prepend && log
        ? Array.from(log.querySelectorAll<HTMLElement>('[data-message-id]')).find(
            (item) => item.getBoundingClientRect().bottom > logTop,
          )
        : undefined;
    const anchorTop = anchor?.getBoundingClientRect().top ?? 0;
    const nearEnd = log && log.scrollHeight - log.scrollTop - log.clientHeight < 80;
    this.rows.set(
      [...stored.values()].sort((a, b) => a.id.length - b.id.length || a.id.localeCompare(b.id)),
    );
    if (log && (anchor || (!prepend && nearEnd))) {
      afterNextRender(
        () => {
          if (!this.active()) return;
          if (anchor) log.scrollTop += anchor.getBoundingClientRect().top - anchorTop;
          else log.scrollTop = log.scrollHeight;
        },
        { injector: this.injector },
      );
    }
  }

  private revoke(): void {
    if (!this.active()) return;
    this.state.set('revoked');
    this.stopReads.next();
    this.syncing.set(false);
    this.historyError.set(false);
    this.olderError.set(false);
    this.before.set(null);
    this.rows.set([]);
    this.draft.reset();
    this.draftAttempt = undefined;
    void this.connection?.stop();
    this.accessLost.emit();
  }

  private active(): boolean {
    return !this.destroyed && this.state() !== 'revoked';
  }

  private hasCode(error: unknown, code: string): boolean {
    return error instanceof Error && error.message.includes(code);
  }
}
