import { hasMinRole } from './roles';

describe('Role hierarchy', () => {
  it.each([
    ['User', 'User', true],
    ['User', 'Moderator', false],
    ['User', 'Admin', false],
    ['Moderator', 'User', true],
    ['Moderator', 'Moderator', true],
    ['Moderator', 'Admin', false],
    ['Admin', 'User', true],
    ['Admin', 'Moderator', true],
    ['Admin', 'Admin', true],
  ])('%s has minimum role %s: %s', (current, minimum, expected) => {
    expect(hasMinRole(current, minimum)).toBe(expected);
  });

  it.each([null, undefined, '', 'Owner', 'admin', 'toString'])(
    'rejects unsupported current role %s',
    (role) => {
      expect(hasMinRole(role, 'User')).toBe(false);
    },
  );

  it.each(['', 'Owner', 'admin', 'toString'])('rejects unsupported minimum role %s', (role) => {
    expect(hasMinRole('Admin', role)).toBe(false);
  });
});
