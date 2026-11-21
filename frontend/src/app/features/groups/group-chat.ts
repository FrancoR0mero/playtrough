import { Dialog, DialogModule } from '@angular/cdk/dialog';
import { DatePipe, DOCUMENT } from '@angular/common';
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
import { filter, firstValueFrom, Subject, takeUntil } from 'rxjs';
import { normalizePlainText } from '../../shared/plain-text';
import { AuthService } from '../auth/services/auth';
import { ChatMessage, GroupChatConnection } from './group-chat-connection';
import { GroupConfirmation, GroupConfirmationComponent } from './group-confirmation';
import { ChatHistoryQuery, GroupsApi } from './groups-api';

@Component({
  selector: 'app-group-chat',
  imports: [DatePipe, DialogModule, ReactiveFormsModule],
  templateUrl: './group-chat.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GroupChatComponent implements OnInit, OnDestroy {
  readonly groupId = input.required<number>();
  readonly owner = input(false);
  readonly accessLost = output<void>();
  private readonly client = inject(GroupChatConnection);
  private readonly api = inject(GroupsApi);
  private readonly auth = inject(AuthService);
  private readonly dialog = inject(Dialog);
  private readonly injector = inject(Injector);
  private readonly browser = inject(DOCUMENT).defaultView;
  private readonly stopReads = new Subject<void>();
  private syncGeneration = 0;
  // Only the paginated HTTP response can confirm a continuous range of IDs.
  private checkpoint: string | null = null;
  private connection?: HubConnection;
  private draftAttempt?: { id: string; text: string };
  private stopping?: Promise<void>;
  private connecting = false;
  private reconnectRequested = false;
  private networkGeneration = 0;
  private receiptTimer?: ReturnType<typeof setTimeout>;
  private readonly receiptQueue = new Set<string>();
  private readonly acknowledged = new Set<string>();
  private readonly delivered = new Set<string>();
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
  protected readonly online = signal(this.browser?.navigator.onLine !== false);
  protected readonly pending = signal<{ id: string; text: string } | null>(null);
  protected readonly removing = signal<string | null>(null);
  protected readonly removalError = signal('');
  protected readonly userId = computed(() => this.auth.currentUser()?.id);

  ngOnInit(): void {
    const connection = this.client.create();
    this.connection = connection;
    connection.on('MessageCreated', (message: ChatMessage) => this.receive(message));
    connection.on('MessageRemoved', (message: ChatMessage) => this.receive(message));
    connection.on('MessagesDelivered', (ids: string[]) => {
      if (!this.active() || !this.isOnline()) return;
      for (const id of ids) this.delivered.add(id);
      this.rows.update((rows) =>
        rows.map((row) => (this.delivered.has(row.id) ? { ...row, delivered: true } : row)),
      );
    });
    connection.on('ChatAccessRevoked', (groupId: number) => {
      if (groupId === this.groupId()) this.revoke();
    });
    connection.onreconnecting(() => {
      if (this.active() && this.isOnline()) this.state.set('reconnecting');
    });
    connection.onreconnected(() => this.subscribe(connection));
    connection.onclose(() => {
      if (this.active()) this.state.set('offline');
    });
    this.browser?.addEventListener('offline', this.goOffline);
    this.browser?.addEventListener('online', this.goOnline);
    void this.connect();
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.browser?.removeEventListener('offline', this.goOffline);
    this.browser?.removeEventListener('online', this.goOnline);
    clearTimeout(this.receiptTimer);
    this.stopReads.next();
    this.stopReads.complete();
    void this.connection?.stop();
  }

  protected async connect(): Promise<void> {
    const connection = this.connection;
    if (!connection || !this.active() || this.connecting) return;
    if (!this.isOnline()) {
      this.state.set('offline');
      return;
    }
    this.connecting = true;
    const generation = this.networkGeneration;
    try {
      if (this.stopping) await this.stopping;
      if (!this.active() || !this.isOnline() || generation !== this.networkGeneration) return;
      this.state.set('connecting');
      this.error.set('');
      await connection.start();
      if (generation === this.networkGeneration) await this.subscribe(connection);
    } catch {
      if (this.active()) this.state.set('offline');
    } finally {
      this.connecting = false;
      if (this.reconnectRequested) {
        this.reconnectRequested = false;
        if (this.state() === 'offline') void this.connect();
      }
    }
  }

  protected send(): void {
    if (
      !this.active() ||
      this.pending() ||
      this.count() < 1 ||
      this.count() > this.settings.maxLength
    )
      return;
    const text = this.clean();
    const attempt =
      this.draftAttempt?.text === text ? this.draftAttempt : { id: crypto.randomUUID(), text };
    this.draftAttempt = attempt;
    this.pending.set(attempt);
    this.draft.reset();
    this.error.set('');
    if (!this.isOnline()) this.goOffline();
    void this.flushPending();
  }

  private async flushPending(): Promise<void> {
    const connection = this.connection;
    const attempt = this.pending();
    if (!connection || !attempt || this.sending() || this.state() !== 'ready' || !this.isOnline())
      return;
    this.sending.set(true);
    try {
      const message = await connection.invoke<ChatMessage>(
        'SendMessage',
        this.groupId(),
        attempt.id,
        attempt.text,
      );
      if (!this.active()) return;
      this.merge([message]);
      this.completeAttempt(attempt.id);
    } catch (error) {
      if (!this.active() || this.pending()?.id !== attempt.id) return;
      if (this.hasCode(error, 'chat_not_available')) this.revoke();
      else if (!this.isOnline() || this.state() !== 'ready') {
        // A reconnect retries the same UUID, even if the first attempt reached the API.
      } else if (this.hasCode(error, 'chat_rate_limited')) {
        this.restoreDraft(attempt.text);
        this.error.set(
          'Enviaste muchos mensajes seguidos. Esperá unos segundos y volvé a intentar.',
        );
      } else if (this.hasCode(error, 'chat_invalid_message')) {
        this.restoreDraft(attempt.text);
        this.error.set('Revisá que el mensaje tenga entre 1 y 2.000 caracteres.');
      } else {
        this.restoreDraft(attempt.text);
        this.error.set('No se confirmó el envío. Podés volver a intentarlo.');
      }
    } finally {
      this.sending.set(false);
      if (this.active() && this.pending() && this.state() === 'ready') void this.flushPending();
    }
  }

  private restoreDraft(text: string): void {
    this.pending.set(null);
    this.draft.setValue(text);
  }

  private completeAttempt(id: string): void {
    if (this.pending()?.id === id) this.pending.set(null);
    if (this.draftAttempt?.id === id) this.draftAttempt = undefined;
  }

  private isOnline(): boolean {
    return this.online() && this.browser?.navigator.onLine !== false;
  }

  private readonly goOffline = () => {
    if (!this.active() || (!this.online() && this.state() === 'offline')) return;
    this.online.set(false);
    this.state.set('offline');
    this.networkGeneration++;
    this.syncGeneration++;
    this.syncing.set(false);
    clearTimeout(this.receiptTimer);
    this.receiptTimer = undefined;
    this.stopping = this.connection?.stop().catch(() => undefined);
  };

  private readonly goOnline = () => {
    if (!this.active()) return;
    this.online.set(true);
    if (this.state() !== 'offline') return;
    if (this.connecting) this.reconnectRequested = true;
    else void this.connect();
  };

  protected canRemove(message: ChatMessage): boolean {
    return !message.removedAt && (this.owner() || message.authorId === this.userId());
  }

  protected confirmRemoval(message: ChatMessage): void {
    if (!this.canRemove(message) || !this.active() || this.removing() !== null) return;
    this.dialog
      .open<boolean, GroupConfirmation>(GroupConfirmationComponent, {
        data: {
          title: 'Retirar mensaje',
          message: 'El texto se reemplazará por un aviso de retirada para los miembros del grupo.',
          label: 'Retirar mensaje',
        },
        ariaLabelledBy: 'group-confirmation-title',
        ariaDescribedBy: 'group-confirmation-description',
      })
      .closed.pipe(filter(Boolean), takeUntil(this.stopReads))
      .subscribe(() => void this.removeMessage(message));
  }

  private async removeMessage(message: ChatMessage): Promise<void> {
    if (!this.active() || this.removing() !== null || !this.canRemove(message)) return;
    this.removing.set(message.id);
    this.removalError.set('');
    try {
      const state = await firstValueFrom(
        this.api.removeChatMessage(this.groupId(), message.id).pipe(takeUntil(this.stopReads)),
        { defaultValue: null },
      );
      if (state?.removedAt && this.active() && this.rows().some((row) => row.id === state.id))
        this.receive({ ...message, text: null, removedAt: state.removedAt });
    } catch (error) {
      if (!this.active()) return;
      this.removalError.set('No pudimos retirar el mensaje. Volvé a intentarlo.');
      if (error instanceof HttpErrorResponse && error.status === 404) await this.syncHistory();
    } finally {
      this.removing.set(null);
    }
  }

  private async subscribe(connection: HubConnection): Promise<void> {
    if (!this.active() || !this.isOnline()) return;
    const generation = this.networkGeneration;
    try {
      await connection.invoke('JoinGroup', this.groupId());
      if (!this.active() || !this.isOnline() || generation !== this.networkGeneration) return;
      this.state.set('ready');
      await this.syncHistory();
      void this.flushPending();
      this.scheduleReceipts();
    } catch (error) {
      if (!this.active() || generation !== this.networkGeneration) return;
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
          await this.reconcileMessages(current);
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
    if (!before || this.olderLoading() || this.syncing() || this.state() !== 'ready') return;
    const generation = this.syncGeneration;
    this.olderLoading.set(true);
    this.olderError.set(false);
    try {
      const page = await this.readHistory({ before });
      if (!page || !this.active() || generation !== this.syncGeneration) return;
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
    if (this.active() && this.isOnline()) this.merge([message]);
  }

  private scheduleReceipts(delay = 50): void {
    if (
      this.receiptTimer !== undefined ||
      !this.receiptQueue.size ||
      this.state() !== 'ready' ||
      !this.isOnline()
    )
      return;
    this.receiptTimer = setTimeout(() => {
      this.receiptTimer = undefined;
      void this.flushReceipts();
    }, delay);
  }

  private async flushReceipts(): Promise<void> {
    if (!this.connection || !this.active() || this.state() !== 'ready' || !this.isOnline()) return;
    const ids = [...this.receiptQueue].slice(0, 100);
    if (!ids.length) return;
    for (const id of ids) {
      this.receiptQueue.delete(id);
      this.acknowledged.add(id);
    }
    let delay = 50;
    try {
      await this.connection.invoke('AcknowledgeMessages', this.groupId(), ids);
    } catch {
      for (const id of ids) {
        this.acknowledged.delete(id);
        this.receiptQueue.add(id);
      }
      delay = 1000;
    }
    if (this.active()) this.scheduleReceipts(delay);
  }

  private async reconcileMessages(current: () => boolean): Promise<void> {
    const ids = this.rows().map((message) => message.id);
    for (let offset = 0; offset < ids.length; offset += 100) {
      const batch = ids.slice(offset, offset + 100);
      const states = await firstValueFrom(
        this.api.chatMessageStates(this.groupId(), batch).pipe(takeUntil(this.stopReads)),
        { defaultValue: null },
      );
      if (!states || !current()) return;
      const checked = new Set(batch);
      const visible = new Map(states.map((state) => [state.id, state]));
      this.rows.update((rows) =>
        rows
          .filter((row) => !checked.has(row.id) || visible.has(row.id))
          .map((row) => {
            const removedAt = visible.get(row.id)?.removedAt;
            const delivered = row.delivered || visible.get(row.id)?.delivered;
            if (delivered) this.delivered.add(row.id);
            return removedAt && !row.removedAt
              ? { ...row, text: null, removedAt, delivered }
              : { ...row, delivered };
          }),
      );
    }
  }

  private merge(messages: ChatMessage[], prepend = false): void {
    const stored = new Map(this.rows().map((item) => [item.id, item]));
    let changed = false;
    for (const message of messages) {
      if (message.groupId !== this.groupId()) continue;
      if (message.authorId === this.userId() && message.clientMessageId)
        this.completeAttempt(message.clientMessageId);
      if (
        message.authorId !== this.userId() &&
        !message.removedAt &&
        !this.acknowledged.has(message.id)
      )
        this.receiptQueue.add(message.id);
      if (message.delivered) this.delivered.add(message.id);
      const existing = stored.get(message.id);
      if (
        !existing ||
        (message.removedAt && !existing.removedAt) ||
        (this.delivered.has(message.id) && !existing.delivered)
      ) {
        const newest = existing?.removedAt ? existing : message;
        stored.set(message.id, { ...newest, delivered: this.delivered.has(message.id) });
        changed = true;
      }
    }
    this.scheduleReceipts();
    if (!changed) return;
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
    this.removalError.set('');
    this.olderError.set(false);
    this.before.set(null);
    this.rows.set([]);
    this.pending.set(null);
    this.receiptQueue.clear();
    this.acknowledged.clear();
    this.delivered.clear();
    clearTimeout(this.receiptTimer);
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
