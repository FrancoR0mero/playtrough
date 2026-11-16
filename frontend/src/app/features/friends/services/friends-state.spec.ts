import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../../environments/environment';
import { createAuthMock, createSession } from '../../auth/auth-test-helpers';
import { AuthService } from '../../auth/services/auth';
import { FRIEND, REQUEST, SUMMARY } from '../friends-test-helpers';
import { FriendsService } from './friends';
import { FriendsState } from './friends-state';

describe('Shared friends summary', () => {
  let http: HttpTestingController;
  let auth: ReturnType<typeof createAuthMock>;
  let state: FriendsState;
  const url = `${environment.apiUrl}/friends`;
  beforeEach(() => {
    auth = createAuthMock();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: auth },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    state = TestBed.inject(FriendsState);
    TestBed.tick();
  });
  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });
  function login() {
    auth.currentUser.set(createSession().user);
    TestBed.tick();
    http.expectOne(`${url}/summary`).flush(SUMMARY);
  }
  it('loads at login, refreshes on focus and clears data at logout', () => {
    http.expectNone(`${url}/summary`);
    login();
    expect(state.pendingReceived()).toBe(3);
    window.dispatchEvent(new Event('focus'));
    http.expectOne(`${url}/summary`).flush({ ...SUMMARY, pendingReceivedCount: 1 });
    expect(state.pendingReceived()).toBe(1);
    auth.currentUser.set(null);
    TestBed.tick();
    expect(state.summary()).toBeNull();
    window.dispatchEvent(new Event('focus'));
    http.expectNone(`${url}/summary`);
  });
  it('refreshes summary after all five actions', () => {
    login();
    const service = TestBed.inject(FriendsService);
    service.send('bea').subscribe();
    http.expectOne(`${url}/requests`).flush(REQUEST);
    http.expectOne(`${url}/summary`).flush(SUMMARY);
    service.accept(7).subscribe();
    http.expectOne(`${url}/requests/7/accept`).flush({ friend: FRIEND.user, since: FRIEND.since });
    http.expectOne(`${url}/summary`).flush(SUMMARY);
    service.reject(7, 'bea').subscribe();
    http.expectOne(`${url}/requests/7/reject`).flush(null);
    http.expectOne(`${url}/summary`).flush(SUMMARY);
    service.cancel(7, 'bea').subscribe();
    http.expectOne(`${url}/requests/7`).flush(null);
    http.expectOne(`${url}/summary`).flush(SUMMARY);
    service.remove('bea').subscribe();
    http.expectOne(`${url}/bea`).flush(null);
    http.expectOne(`${url}/summary`).flush({ ...SUMMARY, friendsCount: 1 });
    expect(state.summary()?.friendsCount).toBe(1);
  });
  it('cancels obsolete summaries so another account never inherits their counters', () => {
    auth.currentUser.set(createSession().user);
    TestBed.tick();
    const old = http.expectOne(`${url}/summary`);
    auth.currentUser.set({ ...createSession().user, id: 2 });
    TestBed.tick();
    expect(old.cancelled).toBe(true);
    http.expectOne(`${url}/summary`).flush({ ...SUMMARY, pendingReceivedCount: 0 });
    expect(state.pendingReceived()).toBe(0);
  });
  it('retains a readable count and exposes retryable summary errors', () => {
    login();
    state.refresh();
    http.expectOne(`${url}/summary`).error(new ProgressEvent('error'));
    expect(state.pendingReceived()).toBe(3);
    expect(state.error()).toContain('No pudimos');
    state.refresh();
    http.expectOne(`${url}/summary`).flush(SUMMARY);
    expect(state.error()).toBeNull();
  });
});
