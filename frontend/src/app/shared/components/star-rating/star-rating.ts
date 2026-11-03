import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  forwardRef,
  inject,
  input,
  signal,
} from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

@Component({
  selector: 'app-star-rating',
  templateUrl: './star-rating.html',
  styleUrl: './star-rating.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [
    { provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => StarRatingComponent), multi: true },
  ],
})
export class StarRatingComponent implements ControlValueAccessor {
  readonly mode = input<'read' | 'input'>('read');
  readonly value = input<number | null>(null);
  readonly size = input(24);
  readonly label = input('Puntuación');
  protected readonly stars = [1, 2, 3, 4, 5];
  protected readonly selected = signal<number | null>(null);
  protected readonly disabled = signal(false);
  protected readonly preview = signal<number | null>(null);
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);
  private onChange: (value: number) => void = () => undefined;
  private onTouched: () => void = () => undefined;

  protected accessibleValue(): string {
    const value = this.value();
    return value === null
      ? 'Sin puntuación'
      : `Puntuación ${this.clamp(value).toLocaleString('es-AR')} de 5`;
  }

  protected fill(star: number): number {
    const value = this.mode() === 'input' ? (this.preview() ?? this.selected()) : this.value();
    return Math.round(Math.max(0, Math.min(1, this.clamp(value ?? 0) - star + 1)) * 100);
  }

  protected choose(star: number): void {
    if (this.disabled()) return;
    this.preview.set(null);
    this.selected.set(star);
    this.onChange(star);
    this.onTouched();
  }

  protected hover(star: number, event: PointerEvent): void {
    if (
      !this.disabled() &&
      event.pointerType !== 'touch' &&
      this.element.nativeElement.ownerDocument.defaultView?.matchMedia?.(
        '(hover: hover) and (pointer: fine)',
      ).matches
    ) {
      this.preview.set(star);
    }
  }

  protected keydown(event: KeyboardEvent, star: number): void {
    if (this.disabled()) return;
    let next: number;
    switch (event.key) {
      case 'ArrowRight':
      case 'ArrowDown':
        next = star === 5 ? 1 : star + 1;
        break;
      case 'ArrowLeft':
      case 'ArrowUp':
        next = star === 1 ? 5 : star - 1;
        break;
      case 'Home':
        next = 1;
        break;
      case 'End':
        next = 5;
        break;
      default:
        return;
    }
    event.preventDefault();
    this.choose(next);
    this.element.nativeElement.querySelectorAll<HTMLButtonElement>('button')[next - 1]?.focus();
  }

  protected blur(event: FocusEvent): void {
    if (!this.element.nativeElement.contains(event.relatedTarget as Node | null)) this.onTouched();
  }

  writeValue(value: number | null): void {
    this.selected.set(value);
    this.preview.set(null);
  }
  registerOnChange(fn: (value: number) => void): void {
    this.onChange = fn;
  }
  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }
  setDisabledState(disabled: boolean): void {
    this.disabled.set(disabled);
    this.preview.set(null);
  }

  private clamp(value: number): number {
    return Number.isFinite(value) ? Math.max(0, Math.min(5, value)) : 0;
  }
}
