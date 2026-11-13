import { HttpErrorResponse, HttpHeaders, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { environment } from '../../../environments/environment';
import { RateLimitError } from '../../core/http/rate-limit-error';
import { createFriendsMock } from '../friends/friends-test-helpers';
import { FriendsService } from '../friends/services/friends';
import { BlockUserButtonComponent } from './block-user-button';
import { BlockedUsersComponent } from './blocked-users';
import { BlockedUser, BlocksService } from './services/blocks';

const BEA: BlockedUser = {
  user: { username: 'bea', avatarUrl: null },
  blockedAt: '2026-11-13T12:00:00Z',
};
const CAMI: BlockedUser = { ...BEA, user: { username: 'cami', avatarUrl: null } };
const page = (items = [BEA], hasMore = false, number = 1) => ({
  items,
  hasMore,
  page: number,
  pageSize: 20,
});

describe('Blocks HTTP service', () => {
  it('uses the authenticated endpoints and publishes social changes only after success', () => {
    const friends = createFriendsMock();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: FriendsService, useValue: friends },
      ],
    });
    const service = TestBed.inject(BlocksService);
    const http = TestBed.inject(HttpTestingController);
    service.list(2, 10).subscribe();
    const list = http.expectOne((req) => req.url === `${environment.apiUrl}/blocks`);
    expect(list.request.params.get('page')).toBe('2');
    expect(list.request.params.get('pageSize')).toBe('10');
    list.flush(page());
    service.block('bea').subscribe();
    const block = http.expectOne(`${environment.apiUrl}/blocks`);
    expect(block.request.method).toBe('POST');
    expect(block.request.body).toEqual({ username: 'bea' });
    expect(friends.notifyBlockChange).not.toHaveBeenCalled();
    block.flush(null, { status: 204, statusText: 'No Content' });
    expect(friends.notifyBlockChange).toHaveBeenCalledWith('bea', 'blocked');
    service.unblock('a b').subscribe();
    const unblock = http.expectOne(`${environment.apiUrl}/blocks/a%20b`);
    expect(unblock.request.method).toBe('DELETE');
    unblock.flush(null, { status: 204, statusText: 'No Content' });
    expect(friends.notifyBlockChange).toHaveBeenCalledWith('a b', 'unblocked');
    service.block('cami').subscribe({ error: () => undefined });
    http
      .expectOne(`${environment.apiUrl}/blocks`)
      .flush(null, { status: 500, statusText: 'Error' });
    expect(friends.notifyBlockChange).toHaveBeenCalledTimes(2);
    http.verify();
  });
});

describe('Block user button', () => {
  function setup() {
    const blocks = { block: vi.fn().mockReturnValue(of(undefined)) };
    TestBed.configureTestingModule({ providers: [{ provide: BlocksService, useValue: blocks }] });
    const fixture = TestBed.createComponent(BlockUserButtonComponent);
    fixture.componentRef.setInput('username', 'bea');
    const done = vi.fn();
    fixture.componentInstance.blocked.subscribe(done);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const click = (label: string) => {
      [...root.querySelectorAll('button')].find((b) => b.textContent?.trim() === label)!.click();
      fixture.detectChanges();
    };
    return { fixture, root, blocks, done, click };
  }
  afterEach(() => {
    TestBed.resetTestingModule();
    vi.useRealTimers();
  });

  it('requires confirmation, explains consequences and restores focus on cancel', async () => {
    const f = setup();
    f.click('Bloquear usuario');
    expect(f.root.textContent).toContain('Se eliminará la amistad');
    expect(f.blocks.block).not.toHaveBeenCalled();
    await f.fixture.whenStable();
    expect(document.activeElement?.getAttribute('data-action')).toBe('confirm-block');
    f.click('Cancelar bloqueo');
    await f.fixture.whenStable();
    expect(document.activeElement?.getAttribute('data-action')).toBe('block-user');
    expect(f.blocks.block).not.toHaveBeenCalled();
  });

  it('prevents duplicate requests and emits success only after the server confirms', () => {
    const f = setup();
    const pending = new Subject<void>();
    f.blocks.block.mockReturnValue(pending);
    f.click('Bloquear usuario');
    f.click('Sí, bloquear');
    f.root.querySelector<HTMLButtonElement>('[data-action="confirm-block"]')!.click();
    expect(f.blocks.block).toHaveBeenCalledExactlyOnceWith('bea');
    expect(f.done).not.toHaveBeenCalled();
    pending.next();
    pending.complete();
    expect(f.done).toHaveBeenCalledTimes(1);
  });

  it('preserves confirmation and allows retry after a network error', () => {
    const f = setup();
    f.blocks.block.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
    f.click('Bloquear usuario');
    f.click('Sí, bloquear');
    expect(f.root.querySelector('[role="alert"]')?.textContent).toContain('Revisá tu conexión');
    expect(f.done).not.toHaveBeenCalled();
    f.blocks.block.mockReturnValue(of(undefined));
    f.click('Sí, bloquear');
    expect(f.done).toHaveBeenCalledTimes(1);
  });

  it('honors Retry-After without resending automatically', () => {
    vi.useFakeTimers();
    const f = setup();
    f.blocks.block.mockReturnValue(
      throwError(
        () =>
          new RateLimitError(
            new HttpErrorResponse({
              status: 429,
              headers: new HttpHeaders({ 'Retry-After': '2' }),
            }),
          ),
      ),
    );
    f.click('Bloquear usuario');
    f.click('Sí, bloquear');
    expect(f.root.querySelector<HTMLButtonElement>('[data-action="confirm-block"]')!.disabled).toBe(
      true,
    );
    vi.advanceTimersByTime(2100);
    f.fixture.detectChanges();
    expect(f.blocks.block).toHaveBeenCalledTimes(1);
    expect(f.root.querySelector<HTMLButtonElement>('[data-action="confirm-block"]')!.disabled).toBe(
      false,
    );
  });
});

describe('Blocked accounts management', () => {
  function setup(initial = page()) {
    const blocks = {
      list: vi.fn().mockReturnValue(of(initial)),
      unblock: vi.fn().mockReturnValue(of(undefined)),
    };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: BlocksService, useValue: blocks },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: convertToParamMap({}) } },
        },
      ],
    });
    const fixture = TestBed.createComponent(BlockedUsersComponent);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const click = (label: string) => {
      [...root.querySelectorAll('button')].find((b) => b.textContent?.trim() === label)!.click();
      fixture.detectChanges();
    };
    return { blocks, fixture, root, click };
  }
  afterEach(() => {
    TestBed.resetTestingModule();
    vi.useRealTimers();
  });

  it('loads only the personal list and provides a meaningful empty state', () => {
    const f = setup(page([]));
    expect(f.blocks.list).toHaveBeenCalledExactlyOnceWith(1);
    expect(f.root.textContent).toContain('No tenés usuarios bloqueados');
    expect(f.root.querySelector('a')?.getAttribute('href')).toBe('/profile/settings/privacy');
  });

  it('cancels unblocking without making a request', () => {
    const f = setup();
    f.click('Desbloquear');
    f.click('Conservar bloqueo');
    expect(f.blocks.unblock).not.toHaveBeenCalled();
    expect(f.root.textContent).toContain('bea');
  });

  it('confirms unblocking, keeps the row until success and reloads pagination', () => {
    const f = setup();
    const pending = new Subject<void>();
    f.blocks.unblock.mockReturnValue(pending);
    f.click('Desbloquear');
    f.click('Sí, desbloquear');
    expect(f.blocks.unblock).toHaveBeenCalledExactlyOnceWith('bea');
    expect(f.root.querySelectorAll('li')).toHaveLength(1);
    f.blocks.list.mockReturnValue(of(page([])));
    pending.next();
    pending.complete();
    f.fixture.detectChanges();
    expect(f.root.querySelectorAll('li')).toHaveLength(0);
    expect(f.root.textContent).toContain('La amistad no se restaura automáticamente');
    expect(f.blocks.list).toHaveBeenCalledTimes(2);
  });

  it('retains blocked users when deleting fails and allows retry', () => {
    const f = setup();
    f.blocks.unblock.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
    f.click('Desbloquear');
    f.click('Sí, desbloquear');
    expect(f.root.querySelectorAll('li')).toHaveLength(1);
    expect(f.root.textContent).toContain('El bloqueo sigue activo');
    expect(
      f.root.querySelector<HTMLButtonElement>('[data-action="confirm-unblock"]')!.disabled,
    ).toBe(false);
  });

  it('retries a failed next page and deduplicates accounts', () => {
    const f = setup(page([BEA], true));
    f.blocks.list.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
    f.click('Cargar más');
    expect(f.root.querySelectorAll('li')).toHaveLength(1);
    f.blocks.list.mockReturnValue(of(page([BEA, CAMI], false, 2)));
    f.click('Reintentar');
    expect(f.blocks.list).toHaveBeenLastCalledWith(2);
    expect(f.root.querySelectorAll('li')).toHaveLength(2);
  });
});
