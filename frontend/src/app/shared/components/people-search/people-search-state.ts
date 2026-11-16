import { Injectable, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl } from '@angular/forms';

@Injectable({ providedIn: 'root' })
export class PeopleSearchState {
  readonly control = new FormControl('', { nonNullable: true });
  readonly invalid = signal(false);
  readonly text = signal('');

  constructor() {
    this.control.valueChanges.pipe(takeUntilDestroyed()).subscribe((value) => {
      this.text.set(value);
      this.invalid.set(false);
    });
  }

  restore(value: string): void {
    this.control.setValue(value, { emitEvent: false });
    this.text.set(value);
  }
}
