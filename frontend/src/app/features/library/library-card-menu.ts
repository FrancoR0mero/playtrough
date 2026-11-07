import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  HostListener,
  Injector,
  afterNextRender,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { LIBRARY_STATUSES, LibraryStatus } from './models/library';

export interface LibraryMenuAction {
  value: LibraryStatus | 'remove';
  origin: HTMLButtonElement;
}

@Component({
  selector: 'app-library-card-menu',
  templateUrl: './library-card-menu.html',
  host: { class: 'absolute top-2 right-2', '(focusout)': 'onFocusOut($event)' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LibraryCardMenuComponent {
  readonly gameId = input.required<number>();
  readonly gameName = input.required<string>();
  readonly status = input.required<LibraryStatus>();
  readonly disabled = input(false);
  readonly chosen = output<LibraryMenuAction>();
  protected readonly opened = signal(false);
  protected readonly states = LIBRARY_STATUSES;
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  protected toggle(): void {
    if (this.disabled()) return;
    if (this.opened()) this.close(true);
    else this.open();
  }

  protected triggerKey(event: KeyboardEvent): void {
    if (this.disabled() || !['ArrowDown', 'ArrowUp', 'Escape'].includes(event.key)) return;
    event.preventDefault();
    event.stopPropagation();
    if (event.key === 'Escape') this.close(true);
    else this.open(event.key === 'ArrowUp');
  }

  private open(last = false): void {
    this.opened.set(true);
    afterNextRender(
      () => {
        const items = this.menuItems();
        items[last ? items.length - 1 : 0]?.focus();
      },
      { injector: this.injector },
    );
  }

  protected menuKey(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      event.preventDefault();
      event.stopPropagation();
      this.close(true);
      return;
    }
    const items = this.menuItems();
    const index = items.indexOf(event.target as HTMLButtonElement);
    const count = items.length;
    if (!count || !['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) return;
    event.preventDefault();
    const next =
      event.key === 'Home'
        ? 0
        : event.key === 'End'
          ? count - 1
          : (index + (event.key === 'ArrowDown' ? 1 : -1) + count) % count;
    items[next]?.focus();
  }

  protected choose(value: LibraryStatus | 'remove'): void {
    if (this.disabled() || value === this.status()) return;
    const origin =
      this.element.nativeElement.querySelector<HTMLButtonElement>('[data-library-menu]')!;
    this.close(true);
    this.chosen.emit({ value, origin });
  }

  protected onFocusOut(event: FocusEvent): void {
    if (event.relatedTarget && !this.element.nativeElement.contains(event.relatedTarget as Node))
      this.close(false);
  }

  @HostListener('document:click', ['$event'])
  protected outsideClick(event: MouseEvent): void {
    if (!this.element.nativeElement.contains(event.target as Node)) this.close(false);
  }

  private close(restore: boolean): void {
    this.opened.set(false);
    if (restore)
      this.element.nativeElement.querySelector<HTMLButtonElement>('[data-library-menu]')?.focus();
  }

  private menuItems(): HTMLButtonElement[] {
    return Array.from(
      this.element.nativeElement.querySelectorAll<HTMLButtonElement>(
        '[role^="menuitem"]:not(:disabled)',
      ),
    );
  }
}
