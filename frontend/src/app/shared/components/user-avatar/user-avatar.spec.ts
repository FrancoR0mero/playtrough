import { TestBed } from '@angular/core/testing';
import { UserAvatarComponent } from './user-avatar';

describe('UserAvatarComponent', () => {
  function setup(username = 'ana') {
    const fixture = TestBed.createComponent(UserAvatarComponent);
    fixture.componentRef.setInput('username', username);
    fixture.detectChanges();
    return fixture;
  }
  it('provides initials, an accessible name and a case-insensitive deterministic color', () => {
    const fixture = setup();
    const avatar = fixture.nativeElement.querySelector('[role="img"]') as HTMLElement;
    expect(avatar.textContent).toBe('AN');
    expect(avatar.getAttribute('aria-label')).toBe('Avatar de ana');
    const color = avatar.style.backgroundColor;
    fixture.componentRef.setInput('username', 'ANA');
    fixture.detectChanges();
    expect(avatar.style.backgroundColor).toBe(color);
  });
  it.each([
    ['sm', '32px'],
    ['md', '44px'],
    ['lg', '96px'],
  ])('supports %s sizing', (size, width) => {
    const fixture = setup();
    fixture.componentRef.setInput('size', size);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('span').style.width).toBe(width);
  });
  it('shows alt text, falls back after an image error, and tries a new URL', () => {
    const fixture = setup();
    fixture.componentRef.setInput('avatarUrl', '/avatar.png');
    fixture.detectChanges();
    const image = fixture.nativeElement.querySelector('img') as HTMLImageElement;
    expect(image.alt).toBe('Avatar de ana');
    image.dispatchEvent(new Event('error'));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('img')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('AN');
    fixture.componentRef.setInput('avatarUrl', '/new.png');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('img').getAttribute('src')).toBe('/new.png');
  });
});
