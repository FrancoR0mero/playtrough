import { TokenStorageService } from './token-storage';
import { createSession as session } from '../auth-test-helpers';

describe('TokenStorageService', () => {
  const storage = new TokenStorageService();
  beforeEach(() => storage.clear());

  afterEach(() => {
    vi.restoreAllMocks();
    storage.clear();
    localStorage.removeItem('unrelated');
  });

  it('saves and reads tokens and user together', () => {
    const value = session();
    storage.save(value);
    expect(storage.read()).toEqual(value);
  });

  it('clears only its own session', () => {
    localStorage.setItem('unrelated', 'preserve');
    storage.save(session());
    storage.clear();
    expect(storage.read()).toBeNull();
    expect(storage.readTokens()).toBeNull();
    expect(localStorage.getItem('playtrough.session')).toBeNull();
    expect(localStorage.getItem('unrelated')).toBe('preserve');
  });

  it.each(['invalid-json', '{}', '{"user":null}', '{"accessToken":42}'])(
    'discards corrupt session %s',
    (raw) => {
      localStorage.setItem('playtrough.session', raw);
      expect(storage.read()).toBeNull();
      expect(localStorage.getItem('playtrough.session')).toBeNull();
    },
  );

  it('discards expired sessions even if a refresh token exists', () => {
    localStorage.setItem(
      'playtrough.session',
      JSON.stringify({ ...session(), expiresAt: new Date(Date.now() - 1).toISOString() }),
    );
    expect(storage.read()).toBeNull();
  });

  it('surfaces save failures and tolerates unavailable storage on read/clear', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('denied');
    });
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('denied');
    });
    vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => {
      throw new Error('denied');
    });
    expect(() => storage.save(session())).toThrow('denied');
    expect(storage.read()).toBeNull();
    expect(storage.readTokens()).toBeNull();
    expect(() => storage.clear()).not.toThrow();
  });

  it('reads only access and refresh tokens for logout', () => {
    // Arrange
    storage.save(session());

    // Act / Assert
    expect(storage.readTokens()).toEqual({ accessToken: 'access', refreshToken: 'refresh' });
  });

  it('can read expired tokens for logout without restoring an expired session', () => {
    // Arrange
    localStorage.setItem(
      'playtrough.session',
      JSON.stringify({ ...session(), expiresAt: new Date(Date.now() - 1).toISOString() }),
    );

    // Act / Assert
    expect(storage.readTokens()).toEqual({ accessToken: 'access', refreshToken: 'refresh' });
    expect(storage.read()).toBeNull();
    expect(storage.readTokens()).toBeNull();
  });

  it.each([
    'invalid-json',
    'null',
    '{}',
    '{"accessToken":"access"}',
    '{"accessToken":42,"refreshToken":"refresh"}',
    '{"accessToken":"access","refreshToken":"   "}',
    '{"accessToken":"","refreshToken":"refresh"}',
  ])('returns null instead of throwing when logout tokens are corrupt: %s', (raw) => {
    // Arrange
    localStorage.setItem('playtrough.session', raw);

    // Act / Assert
    expect(storage.readTokens()).toBeNull();
  });
});
