import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { createAuthMock, createTokenStorageMock } from './features/auth/auth-test-helpers';
import { AuthService } from './features/auth/services/auth';
import { TokenStorageService } from './features/auth/services/token-storage';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: createAuthMock() },
        { provide: TokenStorageService, useValue: createTokenStorageMock() },
      ],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it('should render the base layout shell', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;

    expect(compiled.querySelector('app-layout')).toBeTruthy();
    expect(compiled.querySelector('header')).toBeTruthy();
    expect(compiled.querySelector('footer')).toBeTruthy();
  });
});
