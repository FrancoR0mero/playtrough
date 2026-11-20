import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, Subject, throwError } from 'rxjs';
import { NotificationItem, NotificationPage, NotificationsApi } from './notifications-api';
import { NotificationsCenterComponent } from './notifications-center';
import { NotificationsState } from './notifications-state';

describe('NotificationsCenterComponent', () => {
  const item: NotificationItem = {
    id: '42',
    createdAt: '2026-11-20T00:00:00Z',
    readAt: null,
    message: 'Hay actividad en un grupo.',
    destination: '/groups/2',
  };
  const page: NotificationPage = { items: [item], nextCursor: 'older', readCutoff: 'cutoff' };
  const empty: NotificationPage = { items: [], nextCursor: null, readCutoff: null };

  function setup(initial = of(page)) {
    const list = vi.fn().mockReturnValue(initial);
    const read = vi.fn().mockReturnValue(of(undefined));
    const readAll = vi.fn().mockReturnValue(of({ unreadCount: 0 }));
    const changes = new Subject<number>();
    const refresh = vi.fn(() => changes.next(1));
    TestBed.configureTestingModule({
      imports: [NotificationsCenterComponent],
      providers: [
        provideRouter([]),
        { provide: NotificationsApi, useValue: { list, read, readAll } },
        {
          provide: NotificationsState,
          useValue: { account: signal(1), unreadCount: signal(2), changes, refresh },
        },
      ],
    });
    const fixture = TestBed.createComponent(NotificationsCenterComponent);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const button = (label: string) =>
      [...root.querySelectorAll('button')].find((element) => element.textContent.trim() === label)!;
    return { fixture, root, button, list, read, readAll, refresh };
  }

  beforeEach(() => vi.useFakeTimers());
  afterEach(() => {
    TestBed.resetTestingModule();
    vi.useRealTimers();
  });

  it('preserves the received requests tab in a notification link', () => {
    const { root } = setup(
      of({
        ...page,
        items: [{ ...item, destination: '/friends?tab=received' }],
      }),
    );
    expect(root.querySelector('li a')?.getAttribute('href')).toBe('/friends?tab=received');
  });

  it('appends older rows, links accessible resources and keeps the first page cutoff for bulk read', async () => {
    const { fixture, root, button, list, readAll, refresh } = setup();
    expect(root.querySelector('li a')?.getAttribute('href')).toBe('/groups/2');
    expect(root.querySelector('a[href="/notifications/preferences"]')?.textContent).toContain(
      'Preferencias',
    );
    list.mockReturnValueOnce(
      of({
        ...empty,
        items: [{ ...item, id: '41', destination: null, message: 'Aviso sin detalles' }],
      }),
    );
    button('Cargar anteriores').click();
    fixture.detectChanges();
    expect(list).toHaveBeenLastCalledWith('older');
    expect(root.querySelectorAll('li')).toHaveLength(2);
    expect(root.querySelectorAll('li a')).toHaveLength(1);

    button('Marcar todas como leídas').click();
    expect(readAll).toHaveBeenCalledWith('cutoff');
    expect(refresh).toHaveBeenCalledOnce();
    await vi.advanceTimersByTimeAsync(60);
    expect(list).toHaveBeenLastCalledWith();
  });

  it('shows a load failure, retries, and uses the refreshed page after an individual read', async () => {
    const { fixture, root, button, list, read } = setup(throwError(() => new Error('offline')));
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('No pudimos cargar');
    list.mockReturnValueOnce(of(page));
    button('Reintentar').click();
    await vi.advanceTimersByTimeAsync(60);
    fixture.detectChanges();
    expect(root.querySelector('[role="alert"]')).toBeNull();

    list.mockReturnValueOnce(of({ ...page, items: [{ ...item, readAt: item.createdAt }] }));
    button('Marcar como leída').click();
    expect(read).toHaveBeenCalledWith('42');
    await vi.advanceTimersByTimeAsync(60);
    fixture.detectChanges();
    expect(button('Marcar como leída')).toBeUndefined();
  });
});
