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

export type CommentAction = 'edit' | 'delete';

@Component({
  selector: 'app-comment-menu',
  host: {
    class: 'relative block',
    '(focusout)': 'focusOut($event)',
    '(document:click)': 'outside($event)',
  },
  template: `
    <button
      type="button"
      aria-haspopup="menu"
      [attr.aria-expanded]="open()"
      [attr.aria-label]="'Acciones del comentario de ' + username()"
      (click)="open() ? close(true) : show()"
      (keydown)="triggerKey($event)"
      data-comment-menu
      class="ui-button flex min-h-11 min-w-11 items-center justify-center border border-slate-600 text-slate-200"
    >
      <svg aria-hidden="true" width="20" height="20" viewBox="0 0 24 24" fill="currentColor">
        <circle cx="5" cy="12" r="1.7" />
        <circle cx="12" cy="12" r="1.7" />
        <circle cx="19" cy="12" r="1.7" />
      </svg>
    </button>
    @if (open()) {
      <div
        role="menu"
        tabindex="-1"
        aria-label="Acciones del comentario"
        (keydown)="move($event)"
        class="absolute right-0 z-10 mt-2 min-w-36 rounded-xl border border-slate-600 bg-slate-900 p-1"
      >
        @if (isMine()) {
          <button
            type="button"
            role="menuitem"
            tabindex="-1"
            (click)="choose('edit')"
            class="min-h-11 w-full rounded-lg px-4 text-left text-sm text-white hover:bg-slate-800"
          >
            Editar
          </button>
        }
        @if (canDelete()) {
          <button
            type="button"
            role="menuitem"
            tabindex="-1"
            (click)="choose('delete')"
            class="min-h-11 w-full rounded-lg px-4 text-left text-sm text-red-300 hover:bg-slate-800"
          >
            Eliminar
          </button>
        }
      </div>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CommentMenuComponent {
  readonly username = input.required<string>();
  readonly isMine = input(false);
  readonly canDelete = input(false);
  readonly chosen = output<CommentAction>();
  protected readonly open = signal(false);
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  protected show(last = false): void {
    this.open.set(true);
    afterNextRender(
      () =>
        this.options()
          .at(last ? -1 : 0)
          ?.focus(),
      { injector: this.injector },
    );
  }

  protected close(restore: boolean): void {
    this.open.set(false);
    if (restore)
      this.element.nativeElement.querySelector<HTMLButtonElement>('[data-comment-menu]')?.focus();
  }

  protected triggerKey(event: KeyboardEvent): void {
    if (event.key !== 'ArrowDown' && event.key !== 'ArrowUp' && event.key !== 'Escape') return;
    event.preventDefault();
    if (event.key === 'Escape') this.close(true);
    else this.show(event.key === 'ArrowUp');
  }

  protected move(event: KeyboardEvent): void {
    if (!['ArrowDown', 'ArrowUp', 'Home', 'End', 'Escape'].includes(event.key)) return;
    event.preventDefault();
    if (event.key === 'Escape') {
      this.close(true);
      return;
    }
    const options = this.options();
    const current = options.indexOf(event.target as HTMLButtonElement);
    const next =
      event.key === 'Home'
        ? 0
        : event.key === 'End'
          ? options.length - 1
          : (current + (event.key === 'ArrowDown' ? 1 : -1) + options.length) % options.length;
    options[next]?.focus();
  }

  protected choose(action: CommentAction): void {
    this.close(false);
    this.chosen.emit(action);
  }

  protected focusOut(event: FocusEvent): void {
    if (event.relatedTarget && !this.element.nativeElement.contains(event.relatedTarget as Node))
      this.close(false);
  }

  protected outside(event: MouseEvent): void {
    if (!this.element.nativeElement.contains(event.target as Node)) this.close(false);
  }

  private options(): HTMLButtonElement[] {
    return Array.from(this.element.nativeElement.querySelectorAll('[role="menuitem"]'));
  }
}
