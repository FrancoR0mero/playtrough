import { TokenStorageService } from './token-storage';
import type { LoginResponse } from '../models/login';

describe('TokenStorageService', () => {
  const storage = new TokenStorageService();
  const session = (): LoginResponse => ({
    accessToken: 'access',
    refreshToken: 'refresh',
    expiresAt: new Date(Date.now() + 900_000).toISOString(),
    user: { id: 1, username: 'ana', email: 'ana@example.com', role: 'User' },
  });

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
    expect(() => storage.clear()).not.toThrow();
  });
});
