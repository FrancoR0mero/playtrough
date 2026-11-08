import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../../environments/environment';
import { MY_PROFILE, OTHER_PROFILE } from '../profile-test-helpers';
import { UsersService } from './users';

describe('UsersService', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }),
  );
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('loads the current profile', () => {
    TestBed.inject(UsersService)
      .getMe()
      .subscribe((value) => expect(value).toEqual(MY_PROFILE));
    const request = TestBed.inject(HttpTestingController).expectOne(
      `${environment.apiUrl}/users/me`,
    );
    expect(request.request.method).toBe('GET');
    request.flush(MY_PROFILE);
  });
  it('encodes usernames and accepts omitted restricted fields', () => {
    const restricted = {
      username: 'a/b',
      avatarUrl: null,
      isRestricted: true,
      restriction: 'private',
      relationship: { status: 'none' },
    };
    TestBed.inject(UsersService)
      .getProfile('a/b')
      .subscribe((value) => expect(value).toEqual(restricted));
    const request = TestBed.inject(HttpTestingController).expectOne(
      `${environment.apiUrl}/users/a%2Fb`,
    );
    expect(request.request.method).toBe('GET');
    request.flush(restricted);
  });
  it('loads a public profile', () => {
    TestBed.inject(UsersService)
      .getProfile('jugador')
      .subscribe((value) => expect(value).toEqual(OTHER_PROFILE));
    TestBed.inject(HttpTestingController)
      .expectOne(`${environment.apiUrl}/users/jugador`)
      .flush(OTHER_PROFILE);
  });
  it('sends only the supplied changes, including a cleared biography', () => {
    TestBed.inject(UsersService)
      .updateMe({ bio: null })
      .subscribe((value) => expect(value).toEqual(MY_PROFILE));
    const request = TestBed.inject(HttpTestingController).expectOne(
      `${environment.apiUrl}/users/me`,
    );
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ bio: null });
    request.flush(MY_PROFILE);
  });
});
