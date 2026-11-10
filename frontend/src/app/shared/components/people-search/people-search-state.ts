import { Injectable, signal } from '@angular/core';
import { FormControl } from '@angular/forms';

@Injectable({ providedIn: 'root' })
export class PeopleSearchState {
  readonly control = new FormControl('', { nonNullable: true });
  readonly invalid = signal(false);
}
