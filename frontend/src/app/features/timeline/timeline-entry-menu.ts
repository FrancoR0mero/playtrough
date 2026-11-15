import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { TIMELINE_TEXT } from './timeline-text';

@Component({
  selector: 'app-timeline-entry-menu',
  host: {
    class: 'relative block',
    '(focusout)': 'focusOut($event)',
    '(document:click)': 'outsideClick($event)',
  },
  template: `
    <button
      type="button"
      aria-haspopup="menu"
      [attr.aria-expanded]="opened()"
      [attr.aria-label]="text.actionsFor(gameName())"
      [disabled]="disabled()"
      (click)="toggle()"
      (keydown)="triggerKey($event)"
      data-timeline-menu
      class="ui-button flex min-h-11 min-w-11 items-center justify-center border border-slate-600 text-slate-200 disabled:opacity-50"
    >
      <svg aria-hidden="true" width="20" height="20" viewBox="0 0 24 24" fill="currentColor">
        <circle cx="5" cy="12" r="1.7" />
        <circle cx="12" cy="12" r="1.7" />
        <circle cx="19" cy="12" r="1.7" />
      </svg>
    </button>
    @if (opened()) {
      <div
        role="menu"
        tabindex="-1"
        [attr.aria-label]="text.actionsFor(gameName())"
        (keydown)="menuKey($event)"
        class="absolute right-0 z-10 mt-2 min-w-44 rounded-xl border border-slate-600 bg-slate-900 p-1"
      >
        <button
          type="button"
          role="menuitem"
          [disabled]="disabled()"
          (click)="choose()"
          class="min-h-11 w-full rounded-lg px-4 py-2 text-left text-sm text-white hover:bg-slate-800 focus-visible:outline-2 focus-visible:outline-accent"
        >
          {{ hidden() ? text.show : text.hide }}
        </button>
      </div>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TimelineEntryMenuComponent {
  readonly hidden = input(false);
  readonly gameName = input.required<string>();
  readonly disabled = input(false);
  readonly chosen = output<boolean>();
  protected readonly text = TIMELINE_TEXT;
  protected readonly opened = signal(false);
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  protected toggle(): void {
    if (this.disabled()) return;
    if (this.opened()) this.close(true);
    else this.open();
  }
  private open(): void {
    this.opened.set(true);
    afterNextRender(
      () =>
        this.element.nativeElement.querySelector<HTMLButtonElement>('[role="menuitem"]')?.focus(),
      { injector: this.injector },
    );
  }
  protected triggerKey(event: KeyboardEvent): void {
    if (this.disabled() || !['ArrowUp', 'ArrowDown', 'Escape'].includes(event.key)) return;
    event.preventDefault();
    event.stopPropagation();
    if (event.key === 'Escape') this.close(true);
    else this.open();
  }
  protected menuKey(event: KeyboardEvent): void {
    if (!['ArrowUp', 'ArrowDown', 'Home', 'End', 'Escape'].includes(event.key)) return;
    event.preventDefault();
    event.stopPropagation();
    if (event.key === 'Escape') this.close(true);
    else this.element.nativeElement.querySelector<HTMLButtonElement>('[role="menuitem"]')?.focus();
  }
  protected choose(): void {
    if (!this.disabled()) {
      this.close(true);
      this.chosen.emit(!this.hidden());
    }
  }
  protected focusOut(event: FocusEvent): void {
    if (event.relatedTarget && !this.element.nativeElement.contains(event.relatedTarget as Node))
      this.close(false);
  }
  protected outsideClick(event: MouseEvent): void {
    if (!this.element.nativeElement.contains(event.target as Node)) this.close(false);
  }
  private close(restore: boolean): void {
    this.opened.set(false);
    if (restore) this.focusTrigger();
  }
  focusTrigger(): void {
    this.element.nativeElement.querySelector<HTMLButtonElement>('[data-timeline-menu]')?.focus();
  }
}
