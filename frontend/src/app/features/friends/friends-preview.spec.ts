import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { routes } from '../../app.routes';
import { authGuard } from '../../core/guards/auth-guard';
import { FriendsPreviewComponent } from './friends-preview';
import { FRIEND, createFriendsMock } from './friends-test-helpers';
import { FriendsService } from './services/friends';

describe('Other player friends preview', () => {
  function setup() {
    const service = createFriendsMock();
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: FriendsService, useValue: service }],
    });
    const fixture = TestBed.createComponent(FriendsPreviewComponent);
    fixture.componentRef.setInput('username', 'otro');
    return { fixture, service, root: fixture.nativeElement as HTMLElement };
  }
  it('shows at most six avatars and links to the full list', () => {
    const f = setup();
    f.service.forUser.mockReturnValue(
      of({
        items: Array.from({ length: 8 }, (_, i) => ({
          ...FRIEND,
          user: { username: `jugador${i}`, avatarUrl: null },
        })),
        page: 1,
        pageSize: 6,
        hasMore: true,
      }),
    );
    f.fixture.detectChanges();
    expect(f.service.forUser).toHaveBeenCalledWith('otro', 1, 6);
    expect(f.root.querySelectorAll('app-user-avatar')).toHaveLength(6);
    expect(f.root.querySelector('a')!.getAttribute('href')).toBe('/profile/otro/friends');
  });
  it('reports restriction and refreshes after relationship changes', () => {
    const f = setup();
    f.service.forUser.mockReturnValue(
      throwError(
        () => new HttpErrorResponse({ status: 403, error: { code: 'profile_restricted' } }),
      ),
    );
    f.fixture.detectChanges();
    expect(f.root.querySelector('[role="alert"]')!.textContent).toContain('acceso restringido');
    f.service.forUser.mockReturnValue(
      of({ items: [FRIEND], page: 1, pageSize: 6, hasMore: false }),
    );
    f.service.changes.next({ username: 'otro', action: 'accepted' });
    f.fixture.detectChanges();
    expect(f.root.querySelectorAll('app-user-avatar')).toHaveLength(1);
  });
  it.each(['friends', 'profile/:username/friends'])('protects %s with authGuard', (path) => {
    expect(routes.find((route) => route.path === path)?.canActivate).toEqual([authGuard]);
  });
});
