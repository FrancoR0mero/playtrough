import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Subject, of, throwError } from 'rxjs';
import { createAuthMock, createSession } from '../auth/auth-test-helpers';
import { AuthService } from '../auth/services/auth';
import { createFriendsMock } from '../friends/friends-test-helpers';
import { FriendsService } from '../friends/services/friends';
import { UsersService } from '../profile/services/users';
import { MY_PROFILE, OTHER_PROFILE } from '../profile/profile-test-helpers';
import { ProfileComponent } from '../profile/profile';
import { TimelinePage, TimelinePeriods } from './models/timeline';
import { TimelineService } from './services/timeline';
import { timelineEntry } from './timeline-test-helpers';

describe('Timeline filters, URL and periods', () => {
  const periods: TimelinePeriods = {
    years: [
      {
        year: 2026,
        count: 3,
        months: [
          { month: 3, count: 2 },
          { month: 2, count: 1 },
        ],
      },
      { year: 2025, count: 1, months: [{ month: 12, count: 1 }] },
    ],
  };
  async function setup(
    query = '',
    other = false,
    before?: (service: ReturnType<typeof createService>) => void,
  ) {
    const service = createService();
    before?.(service);
    const auth = createAuthMock();
    auth.currentUser.set(createSession().user);
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'profile', component: ProfileComponent },
          { path: 'profile/:username', component: ProfileComponent },
        ]),
        provideHttpClient(),
        { provide: AuthService, useValue: auth },
        { provide: FriendsService, useValue: createFriendsMock() },
        {
          provide: UsersService,
          useValue: {
            getMe: () => of(MY_PROFILE),
            getProfile: () => of({ ...OTHER_PROFILE, username: 'bea' }),
          },
        },
        { provide: TimelineService, useValue: service },
      ],
    });
    const base = other ? '/profile/bea' : '/profile';
    const harness = await RouterTestingHarness.create(
      `${base}?tab=timeline&keep=yes${query ? '&' + query : ''}`,
    );
    const root = () => harness.routeNativeElement!.querySelector<HTMLElement>('app-timeline')!;
    const router = TestBed.inject(Router);
    const change = async (name: string, value: string) => {
      const select = root().querySelector<HTMLSelectElement>(`[data-filter-${name}]`)!;
      select.value = value;
      select.dispatchEvent(new Event('change'));
      await harness.fixture.whenStable();
      harness.detectChanges();
    };
    return { harness, root, router, service, change, base };
  }
  function createService() {
    return {
      getMine: vi
        .fn()
        .mockReturnValue(of({ items: [timelineEntry()], nextCursor: 'next', hasMore: true })),
      getUser: vi
        .fn()
        .mockReturnValue(of({ items: [timelineEntry()], nextCursor: null, hasMore: false })),
      getMyPeriods: vi.fn().mockReturnValue(of(periods)),
      getUserPeriods: vi.fn().mockReturnValue(of(periods)),
      setVisibility: vi.fn().mockReturnValue(of(timelineEntry({ isHidden: true }))),
    };
  }
  it.each([false, true])(
    'restores all filters and loads periods using only type, other=%s',
    async (other) => {
      const f = await setup('year=2026&month=3&type=added,reviewed', other);
      const expected = { limit: 20, year: 2026, month: 3, type: ['added', 'reviewed'] };
      if (other) {
        expect(f.service.getUser).toHaveBeenCalledExactlyOnceWith('bea', expected);
        expect(f.service.getUserPeriods).toHaveBeenCalledExactlyOnceWith('bea', [
          'added',
          'reviewed',
        ]);
        expect(f.root().querySelector('[data-timeline-menu]')).toBeNull();
      } else {
        expect(f.service.getMine).toHaveBeenCalledExactlyOnceWith(expected);
        expect(f.service.getMyPeriods).toHaveBeenCalledExactlyOnceWith(['added', 'reviewed']);
      }
      expect(f.root().querySelector<HTMLSelectElement>('[data-filter-year]')!.value).toBe('2026');
      expect(f.root().querySelector<HTMLSelectElement>('[data-filter-month]')!.value).toBe('3');
    },
  );
  it('changes year and month through controls, resets the cursor, and preserves the tab', async () => {
    const f = await setup('year=2026&month=3');
    f.root().querySelector<HTMLButtonElement>('[data-timeline-more]')!.click();
    f.harness.detectChanges();
    expect(f.service.getMine).toHaveBeenLastCalledWith({
      limit: 20,
      cursor: 'next',
      year: 2026,
      month: 3,
    });
    await f.change('year', '2025');
    expect(f.router.url).toContain('year=2025');
    expect(f.router.url).toContain('month=3');
    expect(f.router.url).toContain('tab=timeline');
    expect(f.router.url).toContain('keep=yes');
    expect(f.service.getMine).toHaveBeenLastCalledWith({ limit: 20, year: 2025, month: 3 });
    await f.change('month', '12');
    expect(f.service.getMine).toHaveBeenLastCalledWith({ limit: 20, year: 2025, month: 12 });
    expect(f.router.url).toContain('month=12');
    await f.change('year', '');
    expect(f.router.url).not.toContain('year=');
    expect(f.service.getMine).toHaveBeenLastCalledWith({ limit: 20, month: 12 });
  });
  it('updates multi-type URL filters, periods counts and clears everything', async () => {
    const f = await setup();
    for (const type of ['library', 'reviewed']) {
      f.root().querySelector<HTMLButtonElement>(`[data-filter-type="${type}"]`)!.click();
      await f.harness.fixture.whenStable();
      f.harness.detectChanges();
    }
    expect(f.service.getMine).toHaveBeenLastCalledWith({
      limit: 20,
      type: ['added', 'status_changed', 'reviewed'],
    });
    expect(f.service.getMyPeriods).toHaveBeenLastCalledWith([
      'added',
      'status_changed',
      'reviewed',
    ]);
    expect(decodeURIComponent(f.router.url)).toContain('type=added,status_changed,reviewed');
    f.root().querySelector<HTMLButtonElement>('[data-clear-filters]')!.click();
    await f.harness.fixture.whenStable();
    f.harness.detectChanges();
    expect(f.service.getMine).toHaveBeenLastCalledWith({ limit: 20 });
    expect(f.router.url).not.toContain('type=');
  });
  it('cancels both requests on filter change so old data cannot replace new results', async () => {
    const oldRows = new Subject<TimelinePage>(),
      oldPeriods = new Subject<TimelinePeriods>();
    const f = await setup('', false, (service) => {
      service.getMine.mockReturnValueOnce(oldRows);
      service.getMyPeriods.mockReturnValueOnce(oldPeriods);
    });
    await f.router.navigateByUrl('/profile?tab=timeline&type=reviewed');
    f.harness.detectChanges();
    expect(oldRows.observed).toBe(false);
    expect(oldPeriods.observed).toBe(false);
    oldRows.next({
      items: [timelineEntry({ game: { ...timelineEntry().game, name: 'STALE' } })],
      nextCursor: null,
      hasMore: false,
    });
    oldPeriods.next({ years: [{ year: 1999, count: 99, months: [] }] });
    f.harness.detectChanges();
    expect(f.root().textContent).not.toContain('STALE');
    expect(f.root().querySelector('[data-filter-year]')?.textContent).not.toContain('1999');
  });
  it('shows a filtered empty state with a clear action', async () => {
    const f = await setup('year=2026', false, (service) =>
      service.getMine.mockReturnValue(of({ items: [], nextCursor: null, hasMore: false })),
    );
    expect(f.root().textContent).toContain('No hay entradas con estos filtros');
    expect(f.root().querySelector('a[href="/library"]')).toBeNull();
    expect(
      [...f.root().querySelectorAll('button')].filter((b) =>
        b.textContent?.includes('Limpiar filtros'),
      ).length,
    ).toBe(2);
  });
  it.each(['month=13', 'year=2026&type=unknown'])(
    'clears invalid URL filters and announces the recovery: %s',
    async (query) => {
      const f = await setup(query);
      await f.harness.fixture.whenStable();
      f.harness.detectChanges();
      expect(f.router.url).not.toContain('month=');
      expect(f.router.url).not.toContain('year=');
      expect(f.router.url).not.toContain('type=');
      expect(f.root().querySelector('[role="alert"]')?.textContent).toContain(
        'Los filtros no son válidos',
      );
      expect(f.service.getMine).toHaveBeenLastCalledWith({ limit: 20 });
    },
  );
  it('recovers from backend invalid_filter by clearing the URL and reloading', async () => {
    const f = await setup('year=2026', false, (service) =>
      service.getMine.mockReturnValueOnce(
        throwError(() => new HttpErrorResponse({ status: 400, error: { code: 'invalid_filter' } })),
      ),
    );
    await f.harness.fixture.whenStable();
    f.harness.detectChanges();
    expect(f.router.url).not.toContain('year=');
    expect(f.service.getMine).toHaveBeenLastCalledWith({ limit: 20 });
    expect(f.root().textContent).toContain('Los filtros no son válidos');
  });
  it('updates visibility and periods while preserving filters, list and cursor', async () => {
    const f = await setup('year=2026&type=added');
    const reads = f.service.getMine.mock.calls.length,
      counts = f.service.getMyPeriods.mock.calls.length;
    f.root().querySelector<HTMLButtonElement>('[data-timeline-menu]')!.click();
    f.harness.detectChanges();
    f.root().querySelector<HTMLButtonElement>('[role="menuitem"]')!.click();
    f.harness.detectChanges();
    expect(f.root().querySelector('[data-hidden]')).not.toBeNull();
    expect(f.service.getMine).toHaveBeenCalledTimes(reads);
    expect(f.service.getMyPeriods).toHaveBeenCalledTimes(counts + 1);
    expect(f.service.getMyPeriods).toHaveBeenLastCalledWith(['added']);
    expect(f.router.url).toContain('year=2026');
    expect(f.router.url).toContain('type=added');
    f.root().querySelector<HTMLButtonElement>('[data-timeline-more]')!.click();
    f.harness.detectChanges();
    expect(f.service.getMine).toHaveBeenLastCalledWith({
      limit: 20,
      cursor: 'next',
      year: 2026,
      type: ['added'],
    });
  });
  it('removes entry_not_found and announces the removal without changing filters', async () => {
    const f = await setup('year=2026');
    f.service.setVisibility.mockReturnValueOnce(
      throwError(() => new HttpErrorResponse({ status: 404, error: { code: 'entry_not_found' } })),
    );
    f.root().querySelector<HTMLButtonElement>('[data-timeline-menu]')!.click();
    f.harness.detectChanges();
    f.root().querySelector<HTMLButtonElement>('[role="menuitem"]')!.click();
    f.harness.detectChanges();
    expect(f.root().querySelector('ol')).toBeNull();
    expect(f.root().querySelector('[role="alert"]')?.textContent).toContain(
      'La entrada ya no está disponible',
    );
    expect(f.router.url).toContain('year=2026');
  });
  it('handles a periods failure independently and allows retry', async () => {
    const f = await setup('', false, (service) =>
      service.getMyPeriods.mockReturnValueOnce(
        throwError(() => new HttpErrorResponse({ status: 503 })),
      ),
    );
    expect(f.root().querySelector('ol')).not.toBeNull();
    expect(f.root().querySelector('[role="alert"]')?.textContent).toContain(
      'No pudimos cargar los períodos',
    );
    [...f.root().querySelectorAll<HTMLButtonElement>('button')]
      .find((b) => b.textContent?.includes('Reintentar períodos'))!
      .click();
    f.harness.detectChanges();
    expect(f.service.getMyPeriods).toHaveBeenCalledTimes(2);
    expect(f.root().querySelector('[role="alert"]')).toBeNull();
  });
});
