import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { GroupClassificationSummaryComponent } from './group-classification-summary';
import { GroupTagPickerComponent } from './group-tag-picker';
import { GroupClassificationCatalog, GroupFilters, GroupsApi, SocialGroup } from './groups-api';

@Component({
  selector: 'app-groups',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    GroupClassificationSummaryComponent,
    GroupTagPickerComponent,
  ],
  templateUrl: './groups.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GroupsComponent implements OnInit {
  private readonly api = inject(GroupsApi);
  private readonly destroyRef = inject(DestroyRef);
  private request?: Subscription;
  private filters: GroupFilters = { q: '', genreId: '', subgenreId: '', tagIds: [] };

  protected readonly form = new FormGroup({
    q: new FormControl('', { nonNullable: true, validators: Validators.maxLength(100) }),
    genreId: new FormControl('', { nonNullable: true }),
    subgenreId: new FormControl('', { nonNullable: true }),
    tagIds: new FormControl<string[]>([], { nonNullable: true }),
  });
  protected readonly items = signal<SocialGroup[]>([]);
  protected readonly catalog = signal<GroupClassificationCatalog | null>(null);
  protected readonly genreId = toSignal(this.form.controls.genreId.valueChanges, {
    initialValue: '',
  });
  protected readonly subgenres = computed(
    () => this.catalog()?.subgenres.filter((item) => item.genreId === this.genreId()) ?? [],
  );
  protected readonly cursor = signal<number | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal('');
  protected readonly catalogLoading = signal(false);
  protected readonly catalogError = signal(false);

  ngOnInit(): void {
    this.loadCatalog();
    this.load(null);
  }

  protected loadCatalog(): void {
    this.catalogLoading.set(true);
    this.catalogError.set(false);
    this.api
      .classification()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (catalog) => {
          this.catalog.set(catalog);
          this.catalogLoading.set(false);
        },
        error: () => {
          this.catalogError.set(true);
          this.catalogLoading.set(false);
        },
      });
  }

  protected search(): void {
    if (this.form.invalid) return;
    const filters = this.form.getRawValue();
    this.filters = { ...filters, q: filters.q.trim() };
    this.items.set([]);
    this.cursor.set(null);
    this.load(null);
  }

  protected load(cursor: number | null): void {
    this.request?.unsubscribe();
    this.loading.set(true);
    this.error.set('');
    this.request = this.api
      .list(this.filters, cursor)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          this.items.update((items) => (cursor === null ? page.items : [...items, ...page.items]));
          this.cursor.set(page.nextCursor);
          this.loading.set(false);
        },
        error: () => {
          this.error.set('No pudimos cargar los grupos.');
          this.loading.set(false);
        },
      });
  }
}
