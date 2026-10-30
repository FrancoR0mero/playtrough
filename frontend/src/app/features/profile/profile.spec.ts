import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Subject } from 'rxjs';
import { createAuthMock, createSession } from '../auth/auth-test-helpers';
import { AuthService } from '../auth/services/auth';
import { ProfileComponent } from './profile';

describe('Profile account actions', () => {
  it('logs out once, announces the pending state and returns to login', async () => {
    const auth = createAuthMock();
    auth.currentUser.set(createSession().user);
    const pending = new Subject<void>();
    auth.logout.mockReturnValue(pending);
    TestBed.configureTestingModule({
      imports: [ProfileComponent],
      providers: [provideRouter([]), { provide: AuthService, useValue: auth }],
    });
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const fixture = TestBed.createComponent(ProfileComponent);
    fixture.detectChanges();
    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    button.click();
    button.click();
    fixture.detectChanges();
    expect(auth.logout).toHaveBeenCalledTimes(1);
    expect(button.disabled).toBe(true);
    expect(fixture.nativeElement.querySelector('[role="status"]').textContent).toContain(
      'Cerrando sesión',
    );
    pending.complete();
    await fixture.whenStable();
    expect(navigate).toHaveBeenCalledWith('/auth/login', { replaceUrl: true });
    fixture.destroy();
    vi.restoreAllMocks();
  });
});
