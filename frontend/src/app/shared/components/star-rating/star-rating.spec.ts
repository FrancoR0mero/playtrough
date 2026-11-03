import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { StarRatingComponent } from './star-rating';

@Component({
  imports: [ReactiveFormsModule, StarRatingComponent],
  template: '<app-star-rating mode="input" [formControl]="rating" />',
})
class FormHost {
  readonly rating = new FormControl<number | null>(null, Validators.required);
}

describe('StarRatingComponent', () => {
  let fixture: ComponentFixture<StarRatingComponent>;
  const buttons = () =>
    Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[];

  beforeEach(() => {
    fixture = TestBed.createComponent(StarRatingComponent);
    fixture.detectChanges();
  });
  afterEach(() => {
    vi.restoreAllMocks();
    fixture.destroy();
  });

  function input() {
    fixture.componentRef.setInput('mode', 'input');
    fixture.detectChanges();
  }
  function key(index: number, name: string) {
    const event = new KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true });
    buttons()[index].dispatchEvent(event);
    fixture.detectChanges();
    return event;
  }

  it('shows five empty stars and an accessible empty state', () => {
    const img = fixture.nativeElement.querySelector('[role="img"]');
    expect(img.getAttribute('aria-label')).toBe('Sin puntuación');
    expect(img.textContent).toContain('Sin puntuación');
    expect(img.querySelectorAll('svg')).toHaveLength(5);
    expect(buttons()).toHaveLength(0);
    for (const fill of img.querySelectorAll('.star-fill'))
      expect(fill.style.clipPath).toBe('inset(0 100% 0 0)');
  });

  it.each([0, 1, 4.3, 5])('labels %s with Spanish decimals and clips partial fill', (value) => {
    fixture.componentRef.setInput('value', value);
    fixture.componentRef.setInput('size', 32);
    fixture.detectChanges();
    const img = fixture.nativeElement.querySelector('[role="img"]');
    expect(img.getAttribute('aria-label')).toBe(`Puntuación ${value.toLocaleString('es-AR')} de 5`);
    expect(img.textContent).not.toContain('Sin puntuación');
    expect(img.querySelector('svg').getAttribute('width')).toBe('32');
    if (value === 4.3) {
      const fills = img.querySelectorAll('.star-fill');
      expect(fills[3].style.clipPath).toBe('inset(0 0% 0 0)');
      expect(fills[4].style.clipPath).toBe('inset(0 70% 0 0)');
    }
  });

  it('selects by click without deselecting and maintains a single tab stop', () => {
    input();
    const group = fixture.nativeElement.querySelector('[role="radiogroup"]');
    expect(group.getAttribute('aria-required')).toBe('true');
    expect(buttons().map((b) => b.tabIndex)).toEqual([0, -1, -1, -1, -1]);
    expect(buttons().map((b) => b.getAttribute('aria-label'))).toEqual(
      [1, 2, 3, 4, 5].map((n) => `${n} de 5 estrellas`),
    );
    for (let i = 0; i < 2; i++) {
      buttons()[2].click();
      fixture.detectChanges();
      expect(buttons().map((b) => b.getAttribute('aria-checked'))).toEqual([
        'false',
        'false',
        'true',
        'false',
        'false',
      ]);
      expect(buttons().map((b) => b.tabIndex)).toEqual([-1, -1, 0, -1, -1]);
    }
  });

  it.each([
    [0, 'ArrowRight', 1],
    [0, 'ArrowDown', 1],
    [0, 'ArrowLeft', 4],
    [0, 'ArrowUp', 4],
    [4, 'ArrowRight', 0],
    [2, 'Home', 0],
    [2, 'End', 4],
  ] as const)('selects and focuses with %s / %s', (from, name, to) => {
    input();
    expect(key(from, name).defaultPrevented).toBe(true);
    expect(buttons()[to].getAttribute('aria-checked')).toBe('true');
    expect(document.activeElement).toBe(buttons()[to]);
    expect(buttons().filter((b) => b.tabIndex === 0)).toEqual([buttons()[to]]);
  });

  it('leaves Tab to native navigation', () => {
    input();
    expect(key(0, 'Tab').defaultPrevented).toBe(false);
  });

  it.each([
    [true, 'mouse', true],
    [false, 'mouse', false],
    [true, 'touch', false],
  ] as const)('previews only for fine hover pointers: %s / %s', (matches, pointerType, preview) => {
    input();
    vi.stubGlobal('matchMedia', vi.fn().mockReturnValue({ matches }));
    try {
      buttons()[0].click();
      fixture.detectChanges();
      const event = new Event('pointerenter');
      Object.defineProperty(event, 'pointerType', { value: pointerType });
      buttons()[3].dispatchEvent(event);
      fixture.detectChanges();
      expect(buttons()[3].querySelector('path')!.getAttribute('fill')).toBe(
        preview ? 'currentColor' : 'none',
      );
      expect(buttons()[0].getAttribute('aria-checked')).toBe('true');
      fixture.nativeElement
        .querySelector('[role="radiogroup"]')
        .dispatchEvent(new Event('pointerleave'));
      fixture.detectChanges();
      expect(buttons()[3].querySelector('path')!.getAttribute('fill')).toBe('none');
    } finally {
      vi.unstubAllGlobals();
    }
  });
});

describe('StarRatingComponent with Reactive Forms', () => {
  it('supports required validation, model writes, selection, touch, reset and disabling', () => {
    const fixture = TestBed.createComponent(FormHost);
    fixture.detectChanges();
    const control = fixture.componentInstance.rating;
    const buttons = Array.from(
      fixture.nativeElement.querySelectorAll('button'),
    ) as HTMLButtonElement[];
    expect(control.hasError('required')).toBe(true);
    expect(control.touched).toBe(false);
    control.setValue(4);
    fixture.detectChanges();
    expect(buttons[3].getAttribute('aria-checked')).toBe('true');
    expect(control.pristine).toBe(true);
    buttons[1].click();
    fixture.detectChanges();
    expect(control.value).toBe(2);
    expect(control.valid).toBe(true);
    expect(control.touched).toBe(true);
    expect(control.dirty).toBe(true);
    control.disable();
    fixture.detectChanges();
    expect(buttons.every((b) => b.disabled && b.tabIndex === -1)).toBe(true);
    buttons[4].click();
    buttons[1].dispatchEvent(new KeyboardEvent('keydown', { key: 'End', bubbles: true }));
    expect(control.value).toBe(2);
    control.enable();
    control.reset();
    fixture.detectChanges();
    expect(control.hasError('required')).toBe(true);
    expect(buttons[0].tabIndex).toBe(0);
    buttons[0].focus();
    buttons[0].dispatchEvent(new FocusEvent('focusout', { bubbles: true, relatedTarget: null }));
    expect(control.touched).toBe(true);
    fixture.destroy();
  });
});
