import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { createAuthMock, createSession } from '../../../features/auth/auth-test-helpers';
import { AuthService } from '../../../features/auth/services/auth';
import { createFriendsStateMock } from '../../../features/friends/friends-test-helpers';
import { FriendsState } from '../../../features/friends/services/friends-state';
import { NotificationsState } from '../../../features/notifications/notifications-state';
import { LayoutComponent } from './layout';

describe('Feed navigation', () => {
  it('places Inicio first in both navigation bars and hides it for guests', () => {
    const auth = createAuthMock();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        { provide: AuthService, useValue: auth },
        { provide: FriendsState, useValue: createFriendsStateMock() },
        { provide: NotificationsState, useValue: { unreadCount: () => 0 } },
      ],
    });
    const fixture = TestBed.createComponent(LayoutComponent);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('a[href="/feed"]')).toBeNull();
    auth.isAuthenticated.set(true);
    auth.currentUser.set(createSession().user);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('#primary-navigation ul a')?.getAttribute('href')).toBe('/feed');
    expect(root.querySelector('.mobile-game-navigation a')?.getAttribute('href')).toBe('/feed');
    expect(root.querySelectorAll('.mobile-game-navigation a')).toHaveLength(6);
  });
});
