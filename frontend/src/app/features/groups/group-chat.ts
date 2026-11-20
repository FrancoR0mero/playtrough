import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
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
import { normalizePlainText } from '../../shared/plain-text';
import { AuthService } from '../auth/services/auth';
import { ChatMessage, GroupChatConnection } from './group-chat-connection';

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
  private readonly auth = inject(AuthService);
  private connection?: HubConnection;
  private draftAttempt?: { id: string; text: string };
  private destroyed = false;
  private readonly log = viewChild<ElementRef<HTMLElement>>('messageLog');

  protected readonly settings = { maxLength: 2000, retainedMessages: 200 };
  protected readonly draft = new FormControl('', { nonNullable: true });
  private readonly draftValue = toSignal(this.draft.valueChanges, { initialValue: '' });
  protected readonly clean = computed(() => normalizePlainText(this.draftValue()));
  protected readonly count = computed(() => Array.from(this.clean()).length);
  protected readonly rows = signal<ChatMessage[]>([]);
  protected readonly state = signal<
    'connecting' | 'ready' | 'reconnecting' | 'offline' | 'revoked'
  >('connecting');
  protected readonly error = signal('');
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
      if (this.active()) this.state.set('ready');
    } catch (error) {
      if (!this.active()) return;
      if (this.hasCode(error, 'chat_not_available')) this.revoke();
      else {
        await connection.stop();
        if (this.active()) this.state.set('offline');
      }
    }
  }

  private receive(message: ChatMessage): void {
    if (
      !this.active() ||
      message.groupId !== this.groupId() ||
      this.rows().some((item) => item.id === message.id)
    )
      return;
    const log = this.log()?.nativeElement;
    const nearEnd = log && log.scrollHeight - log.scrollTop - log.clientHeight < 80;
    this.rows.update((items) =>
      [...items, message]
        .sort((a, b) => a.id.length - b.id.length || a.id.localeCompare(b.id))
        .slice(-this.settings.retainedMessages),
    );
    if (nearEnd)
      requestAnimationFrame(() => {
        if (this.active()) log.scrollTop = log.scrollHeight;
      });
  }

  private revoke(): void {
    if (!this.active()) return;
    this.state.set('revoked');
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
