import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { PeopleSearchState } from './people-search-state';

@Component({
  selector: 'app-people-search',
  imports: [ReactiveFormsModule],
  template: `
    <form role="search" aria-label="Buscar personas" (submit)="submit($event)">
      <label class="sr-only" for="header-people-search">Buscar personas</label>
      <div class="search-field">
        @if (!state.text()) {
          <svg aria-hidden="true" viewBox="0 0 24 24">
            <circle cx="10.5" cy="10.5" r="6.5" />
            <path d="m16 16 4 4" />
          </svg>
        }
        <input
          id="header-people-search"
          type="search"
          [formControl]="state.control"
          placeholder="Buscar personas…"
          autocomplete="off"
          enterkeyhint="search"
          aria-describedby="header-people-hint"
          [attr.aria-invalid]="state.invalid()"
          [class.with-icon]="!state.text()"
        />
      </div>
      <span id="header-people-hint" class="sr-only"
        >Escribí al menos 2 caracteres y presioná Enter para buscar personas.</span
      >
      <button class="sr-only" type="submit">Buscar personas</button>
    </form>
  `,
  styles: `
    :host {
      display: block;
      min-width: 0;
    }
    .search-hint {
      display: block;
      margin-top: 8px;
      font-size: 12px;
      color: #fca5a5;
    }
    .search-field {
      position: relative;
    }
    input {
      width: 100%;
      min-height: 44px;
      border: 1px solid var(--line);
      border-radius: 999px;
      padding: 10px 16px;
      background: var(--color-background);
      color: white;
      font: inherit;
      font-size: 14px;
    }
    input::placeholder {
      color: #a8a8b5;
    }
    input.with-icon {
      padding-left: 42px;
    }
    input:focus-visible {
      outline: 2px solid var(--color-accent);
      outline-offset: 3px;
    }
    svg {
      position: absolute;
      left: 16px;
      top: 13px;
      width: 18px;
      height: 18px;
      fill: none;
      stroke: #a8a8b5;
      stroke-width: 1.7;
      stroke-linecap: round;
      pointer-events: none;
    }
    button:focus-visible {
      position: static;
      width: auto;
      height: auto;
      clip-path: none;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PeopleSearchComponent {
  protected readonly state = inject(PeopleSearchState);
  private readonly router = inject(Router);

  protected submit(event: Event): void {
    event.preventDefault();
    const q = this.state.control.value.trim();
    if (Array.from(q).length < 2) {
      this.state.invalid.set(true);
      return;
    }
    this.state.invalid.set(false);
    void this.router.navigate(['/people'], { queryParams: { q } });
  }
}
