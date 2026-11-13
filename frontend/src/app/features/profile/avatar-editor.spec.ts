import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';
import { AvatarEditorComponent } from './avatar-editor';
import { UsersService } from './services/users';
import { RateLimitError } from '../../core/http/rate-limit-error';

describe('Avatar editor', () => {
  function setup(url: string | null = null) {
    const users = {
      uploadAvatar: vi.fn().mockReturnValue(of({ avatarUrl: '/api/avatars/new.webp' })),
      deleteAvatar: vi.fn().mockReturnValue(of(undefined)),
    };
    const create = vi.fn().mockReturnValue('blob:preview');
    const revoke = vi.fn();
    Object.defineProperty(URL, 'createObjectURL', { configurable: true, value: create });
    Object.defineProperty(URL, 'revokeObjectURL', { configurable: true, value: revoke });
    TestBed.configureTestingModule({ providers: [{ provide: UsersService, useValue: users }] });
    const fixture = TestBed.createComponent(AvatarEditorComponent);
    fixture.componentRef.setInput('username', 'ana');
    fixture.componentRef.setInput('avatarUrl', url);
    const changed = vi.fn();
    const busy = vi.fn();
    fixture.componentInstance.changed.subscribe(changed);
    fixture.componentInstance.busyChanged.subscribe(busy);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const click = (action: string) => {
      root.querySelector<HTMLButtonElement>(`[data-action="${action}"]`)!.click();
      fixture.detectChanges();
    };
    const select = (file: File) => {
      const input = root.querySelector<HTMLInputElement>('input[type="file"]')!;
      Object.defineProperty(input, 'files', { configurable: true, value: [file] });
      input.dispatchEvent(new Event('change'));
      fixture.detectChanges();
    };
    return { fixture, root, users, changed, busy, create, revoke, click, select };
  }

  afterEach(() => {
    TestBed.resetTestingModule();
    vi.useRealTimers();
  });

  it('previews locally, uploads once, announces success and releases its preview', () => {
    const f = setup();
    const pending = new Subject<{ avatarUrl: string }>();
    f.users.uploadAvatar.mockReturnValue(pending);
    const file = new File(['image'], 'photo.png', { type: 'image/png' });
    f.select(file);
    expect(f.users.uploadAvatar).not.toHaveBeenCalled();
    expect(f.root.querySelector('img')?.alt).toBe('Vista previa de tu nueva foto');
    f.click('save-avatar');
    f.click('save-avatar');
    expect(f.users.uploadAvatar).toHaveBeenCalledExactlyOnceWith(file);
    expect(f.busy).toHaveBeenLastCalledWith(true);
    pending.next({ avatarUrl: '/api/avatars/new.webp' });
    pending.complete();
    f.fixture.detectChanges();
    expect(f.changed).toHaveBeenCalledWith('/api/avatars/new.webp');
    expect(f.revoke).toHaveBeenCalledWith('blob:preview');
    expect(f.root.textContent).toContain('Foto actualizada');
    expect(f.busy).toHaveBeenLastCalledWith(false);
  });

  it.each([
    ['photo.svg', 'image/svg+xml', 'JPG, PNG o WebP'],
    ['photo.gif', 'image/gif', 'JPG, PNG o WebP'],
    ['photo.txt', 'image/png', 'JPG, PNG o WebP'],
  ])('rejects %s before creating a preview', (name, type, message) => {
    const f = setup();
    f.select(new File(['invalid'], name, { type }));
    expect(f.root.textContent).toContain(message);
    expect(f.create).not.toHaveBeenCalled();
    expect(f.users.uploadAvatar).not.toHaveBeenCalled();
  });

  it('rejects empty and oversized files', () => {
    const f = setup();
    f.select(new File([], 'empty.png', { type: 'image/png' }));
    expect(f.root.textContent).toContain('Elegí una imagen');
    f.select(new File([new Uint8Array(5 * 1024 * 1024 + 1)], 'large.png', { type: 'image/png' }));
    expect(f.root.textContent).toContain('hasta 5 MB');
    expect(f.create).not.toHaveBeenCalled();
  });

  it('preserves the selected photo after a server error and permits retry', () => {
    const f = setup();
    f.users.uploadAvatar.mockReturnValue(
      throwError(
        () => new HttpErrorResponse({ status: 400, error: { code: 'invalid_avatar_dimensions' } }),
      ),
    );
    f.select(new File(['image'], 'photo.png', { type: 'image/png' }));
    f.click('save-avatar');
    expect(f.root.textContent).toContain('64 y 4096');
    expect(f.root.querySelector('img')?.getAttribute('src')).toBe('blob:preview');
    expect(f.changed).not.toHaveBeenCalled();
    f.users.uploadAvatar.mockReturnValue(of({ avatarUrl: '/api/avatars/new.webp' }));
    f.click('save-avatar');
    expect(f.changed).toHaveBeenCalledWith('/api/avatars/new.webp');
  });

  it('requires confirmation to delete and emits null for the default avatar', () => {
    const f = setup('/api/avatars/current.webp');
    f.click('remove-avatar');
    expect(f.users.deleteAvatar).not.toHaveBeenCalled();
    f.click('confirm-remove-avatar');
    expect(f.users.deleteAvatar).toHaveBeenCalledTimes(1);
    expect(f.changed).toHaveBeenCalledWith(null);
    expect(f.root.textContent).toContain('Volvés a usar tus iniciales');
  });

  it('blocks retries during 429 without resending automatically', () => {
    vi.useFakeTimers();
    const f = setup();
    f.users.uploadAvatar.mockReturnValue(
      throwError(
        () =>
          new RateLimitError(
            new HttpErrorResponse({
              status: 429,
              headers: new HttpHeaders({ 'Retry-After': '2' }),
            }),
          ),
      ),
    );
    f.select(new File(['image'], 'photo.png', { type: 'image/png' }));
    f.click('save-avatar');
    expect(f.root.querySelector<HTMLButtonElement>('[data-action="save-avatar"]')!.disabled).toBe(
      true,
    );
    vi.advanceTimersByTime(2100);
    f.fixture.detectChanges();
    expect(f.users.uploadAvatar).toHaveBeenCalledTimes(1);
    expect(f.root.querySelector<HTMLButtonElement>('[data-action="save-avatar"]')!.disabled).toBe(
      false,
    );
  });

  it('releases abandoned previews and reports unreadable images', () => {
    const f = setup();
    f.select(new File(['image'], 'photo.png', { type: 'image/png' }));
    f.root.querySelector('img')!.dispatchEvent(new Event('error'));
    f.fixture.detectChanges();
    expect(f.root.textContent).toContain('No pudimos leer');
    expect(f.root.querySelector('[data-action="save-avatar"]')).toBeNull();
    expect(f.revoke).toHaveBeenCalledTimes(1);
    f.select(new File(['other'], 'next.png', { type: 'image/png' }));
    f.fixture.destroy();
    expect(f.revoke).toHaveBeenCalledTimes(2);
  });
});
