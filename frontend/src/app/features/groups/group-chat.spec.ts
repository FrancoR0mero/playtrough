import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Observable, of, Subject, throwError } from 'rxjs';
import { AuthService } from '../auth/services/auth';
import { ChatMessage, GroupChatConnection } from './group-chat-connection';
import { GroupChatComponent } from './group-chat';
import { ChatHistoryPage, ChatHistoryQuery, GroupsApi } from './groups-api';

const message: ChatMessage = {
  id: '47',
  groupId: 42,
  authorId: 7,
  username: 'julieta',
  text: 'Hola 🎮',
  createdAt: '2026-11-19T12:00:00Z',
};

describe('Group live chat', () => {
  async function setup(
    initial: Observable<ChatHistoryPage> = of({ items: [], nextCursor: null, through: '0' }),
  ) {
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
    const history = vi
      .fn((_group: number, query?: ChatHistoryQuery): Observable<ChatHistoryPage> =>
        of({
          items: [],
          nextCursor: null,
          through: query && 'after' in query ? query.after : null,
        }),
      )
      .mockReturnValueOnce(initial);
    TestBed.configureTestingModule({
      providers: [
        { provide: GroupChatConnection, useValue: { create: () => connection } },
        { provide: AuthService, useValue: { currentUser: () => ({ id: 7 }) } },
        { provide: GroupsApi, useValue: { chatHistory: history } },
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
    return { fixture, root, textarea, button, submit, connection, events, accessLost, history };
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

  it('joins before reading history and merges overlapping live messages with older pages', async () => {
    const initial = new Subject<ChatHistoryPage>();
    const { fixture, root, connection, history, events } = await setup(initial);
    expect(connection.invoke).toHaveBeenCalledWith('JoinGroup', 42);
    expect(history).toHaveBeenCalledWith(42, undefined);
    events.get('MessageCreated')!(message);
    initial.next({ items: [{ ...message, id: '46' }, message], nextCursor: '46', through: '47' });
    await fixture.whenStable();
    fixture.detectChanges();
    const list = root.querySelector<HTMLElement>('[role="log"]')!;
    const anchor = list.querySelector<HTMLElement>('[data-message-id="46"]')!;
    list.scrollTop = 30;
    vi.spyOn(anchor, 'getBoundingClientRect').mockImplementation(
      () => new DOMRect(0, list.querySelector('[data-message-id="44"]') ? 220 : 100, 100, 30),
    );
    history.mockReturnValueOnce(
      of({
        items: [
          { ...message, id: '44' },
          { ...message, id: '45' },
        ],
        nextCursor: null,
        through: null,
      }),
    );
    [...root.querySelectorAll('button')]
      .find((item) => item.textContent?.trim() === 'Cargar mensajes anteriores')!
      .click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(history).toHaveBeenLastCalledWith(42, { before: '46' });
    expect([...list.querySelectorAll('li')].map((row) => row.dataset['messageId'])).toEqual([
      '44',
      '45',
      '46',
      '47',
    ]);
    expect(list.scrollTop).toBe(150);
  });

  it('recovers every page from the confirmed checkpoint even if live delivery already reached a newer ID', async () => {
    const { fixture, root, connection, history, events } = await setup(
      of({ items: [message], nextCursor: null, through: '47' }),
    );
    connection.onreconnecting.mock.calls[0][0]();
    events.get('MessageCreated')!({ ...message, id: '50' });
    history
      .mockReturnValueOnce(
        of({ items: [{ ...message, id: '48' }], nextCursor: '48', through: '50' }),
      )
      .mockReturnValueOnce(
        of({
          items: [
            { ...message, id: '49' },
            { ...message, id: '50' },
          ],
          nextCursor: null,
          through: '50',
        }),
      );
    await connection.onreconnected.mock.calls[0][0]();
    fixture.detectChanges();
    expect(history.mock.calls.slice(1)).toEqual([
      [42, { after: '47', through: undefined }],
      [42, { after: '48', through: '50' }],
    ]);
    expect(
      [...root.querySelectorAll<HTMLElement>('[data-message-id]')].map(
        (row) => row.dataset['messageId'],
      ),
    ).toEqual(['47', '48', '49', '50']);

    await connection.onreconnected.mock.calls[0][0]();
    expect(history).toHaveBeenLastCalledWith(42, { after: '50', through: undefined });
  });

  it('clears buffered messages when the history request detects revoked access', async () => {
    const { fixture, root, connection, history, accessLost } = await setup(
      of({ items: [message], nextCursor: null, through: '47' }),
    );
    history.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 404 })));
    await connection.onreconnected.mock.calls[0][0]();
    fixture.detectChanges();
    expect(root.querySelectorAll('[data-message-id]')).toHaveLength(0);
    expect(accessLost).toHaveBeenCalledOnce();
  });
});
