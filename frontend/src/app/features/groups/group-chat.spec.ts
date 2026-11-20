import { TestBed } from '@angular/core/testing';
import { AuthService } from '../auth/services/auth';
import { ChatMessage, GroupChatConnection } from './group-chat-connection';
import { GroupChatComponent } from './group-chat';

const message: ChatMessage = {
  id: '47',
  groupId: 42,
  authorId: 7,
  username: 'julieta',
  text: 'Hola 🎮',
  createdAt: '2026-11-19T12:00:00Z',
};

describe('Group live chat', () => {
  async function setup() {
    const events = new Map<string, (value: ChatMessage | number) => void>();
    const connection = {
      on: vi.fn((name: string, callback: (value: ChatMessage | number) => void) =>
        events.set(name, callback),
      ),
      onclose: vi.fn(),
      onreconnecting: vi.fn(),
      onreconnected: vi.fn(),
      start: vi.fn().mockResolvedValue(undefined),
      stop: vi.fn().mockResolvedValue(undefined),
      invoke: vi.fn().mockResolvedValue(undefined),
    };
    TestBed.configureTestingModule({
      providers: [
        { provide: GroupChatConnection, useValue: { create: () => connection } },
        { provide: AuthService, useValue: { currentUser: () => ({ id: 7 }) } },
      ],
    });
    const fixture = TestBed.createComponent(GroupChatComponent);
    const root = fixture.nativeElement as HTMLElement;
    const accessLost = vi.fn();
    fixture.componentInstance.accessLost.subscribe(accessLost);
    fixture.componentRef.setInput('groupId', 42);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const textarea = root.querySelector('textarea')!;
    const button = () => root.querySelector<HTMLButtonElement>('button[type="submit"]')!;
    const submit = () =>
      root.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    return { fixture, root, textarea, button, submit, connection, events, accessLost };
  }

  it('preserves the draft and reuses the same send ID when delivery is uncertain', async () => {
    const { fixture, root, textarea, button, submit, connection, events } = await setup();
    expect(connection.invoke).toHaveBeenCalledWith('JoinGroup', 42);
    expect(button().disabled).toBe(true);
    textarea.value = '  Hola 🎮 ';
    textarea.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();

    connection.invoke.mockRejectedValueOnce(new Error('chat_rate_limited'));
    submit();
    await fixture.whenStable();
    fixture.detectChanges();
    const first = connection.invoke.mock.calls[1];
    expect(first[0]).toBe('SendMessage');
    expect(first[3]).toBe('Hola 🎮');
    expect(root.textContent).toContain('Esperá unos segundos');
    expect(textarea.value).toContain('Hola 🎮');

    connection.invoke.mockResolvedValueOnce(message);
    submit();
    await fixture.whenStable();
    events.get('MessageCreated')!(message);
    fixture.detectChanges();
    expect(connection.invoke.mock.calls[2]).toEqual(first);
    expect(root.querySelectorAll('[role="log"] li')).toHaveLength(1);
    expect(textarea.value).toBe('');
  });

  it('resubscribes after reconnect and clears displayed chat if access is revoked', async () => {
    const { fixture, root, connection, events, accessLost } = await setup();
    events.get('MessageCreated')!(message);
    fixture.detectChanges();
    expect(root.textContent).toContain('Hola 🎮');
    connection.onreconnecting.mock.calls[0][0]();
    fixture.detectChanges();
    expect(root.textContent).toContain('Reconectando');
    await connection.onreconnected.mock.calls[0][0]();
    fixture.detectChanges();
    expect(connection.invoke.mock.calls).toEqual([
      ['JoinGroup', 42],
      ['JoinGroup', 42],
    ]);

    events.get('ChatAccessRevoked')!(42);
    fixture.detectChanges();
    expect(root.textContent).toContain('Ya no tenés acceso');
    expect(root.textContent).not.toContain('Hola 🎮');
    expect(root.querySelector('textarea')).toBeNull();
    expect(connection.stop).toHaveBeenCalledOnce();
    expect(accessLost).toHaveBeenCalledOnce();
  });
});
