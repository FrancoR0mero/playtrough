import { ComponentFixture, TestBed } from '@angular/core/testing';
import { LibraryCardMenuComponent } from './library-card-menu';

describe('Library card menu accessibility', () => {
  let fixture: ComponentFixture<LibraryCardMenuComponent>;
  let chosen = vi.fn();
  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [LibraryCardMenuComponent] });
    fixture = TestBed.createComponent(LibraryCardMenuComponent);
    fixture.componentRef.setInput('gameId', 17);
    fixture.componentRef.setInput('gameName', 'Portal 2');
    fixture.componentRef.setInput('status', 'playing');
    chosen = vi.fn();
    fixture.componentInstance.chosen.subscribe(chosen);
    fixture.detectChanges();
  });
  afterEach(() => fixture.destroy());
  function trigger(): HTMLButtonElement {
    return fixture.nativeElement.querySelector('[data-library-menu]');
  }
  function key(target: HTMLElement, value: string): void {
    target.dispatchEvent(
      new KeyboardEvent('keydown', { key: value, bubbles: true, cancelable: true }),
    );
    fixture.detectChanges();
  }
  function items(): HTMLButtonElement[] {
    return Array.from(fixture.nativeElement.querySelectorAll('[role^="menuitem"]:not(:disabled)'));
  }

  it('opens with arrows, skips the current disabled state and wraps focus with Home/End', () => {
    expect(trigger().getAttribute('aria-haspopup')).toBe('menu');
    expect(trigger().getAttribute('aria-expanded')).toBe('false');
    key(trigger(), 'ArrowDown');
    expect(trigger().getAttribute('aria-expanded')).toBe('true');
    const current = fixture.nativeElement.querySelector(
      '[data-status="playing"]',
    ) as HTMLButtonElement;
    expect(current.disabled).toBe(true);
    expect(current.getAttribute('aria-checked')).toBe('true');
    expect(document.activeElement).toBe(items()[0]);
    key(items()[0], 'ArrowUp');
    expect(document.activeElement).toBe(items().at(-1));
    key(items().at(-1)!, 'ArrowDown');
    expect(document.activeElement).toBe(items()[0]);
    key(items()[0], 'End');
    expect(document.activeElement).toBe(items().at(-1));
    key(items().at(-1)!, 'Home');
    expect(document.activeElement).toBe(items()[0]);
  });

  it('closes on Escape, restores the trigger and emits no action', () => {
    key(trigger(), 'ArrowUp');
    expect(document.activeElement).toBe(items().at(-1));
    key(items().at(-1)!, 'Escape');
    expect(fixture.nativeElement.querySelector('[role="menu"]')).toBeNull();
    expect(document.activeElement).toBe(trigger());
    expect(chosen).not.toHaveBeenCalled();
  });

  it('chooses a status through the keyboard-activated native button and closes', () => {
    key(trigger(), 'ArrowDown');
    items()[0].dispatchEvent(new MouseEvent('click', { bubbles: true, detail: 0 }));
    fixture.detectChanges();
    expect(chosen).toHaveBeenCalledExactlyOnceWith({ value: 'backlog', origin: trigger() });
    expect(trigger().getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(trigger());
  });

  it('closes on an outside click and on focus leaving the menu', () => {
    trigger().click();
    fixture.detectChanges();
    document.body.click();
    fixture.detectChanges();
    expect(trigger().getAttribute('aria-expanded')).toBe('false');
    trigger().click();
    fixture.detectChanges();
    items()[0].dispatchEvent(
      new FocusEvent('focusout', { bubbles: true, relatedTarget: document.body }),
    );
    fixture.detectChanges();
    expect(trigger().getAttribute('aria-expanded')).toBe('false');
  });

  it('does not open while disabled', () => {
    fixture.componentRef.setInput('disabled', true);
    fixture.detectChanges();
    trigger().click();
    key(trigger(), 'ArrowDown');
    expect(fixture.nativeElement.querySelector('[role="menu"]')).toBeNull();
  });
});
