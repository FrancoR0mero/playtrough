import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { GroupsApi, SocialGroup } from './groups-api';

@Component({
  selector: 'app-groups',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './groups.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GroupsComponent implements OnInit {
  private readonly api = inject(GroupsApi);
  private readonly destroyRef = inject(DestroyRef);
  private request?: Subscription;
  private filters = { q: '', topic: '' };

  protected readonly form = new FormGroup({
    q: new FormControl('', { nonNullable: true, validators: Validators.maxLength(100) }),
    topic: new FormControl('', { nonNullable: true }),
  });
  protected readonly items = signal<SocialGroup[]>([]);
  protected readonly topics = signal<string[]>([]);
  protected readonly cursor = signal<number | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal('');
  protected readonly topicsError = signal(false);

  ngOnInit(): void {
    this.loadTopics();
    this.load(null);
  }

  protected loadTopics(): void {
    this.topicsError.set(false);
    this.api
      .topics()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (topics) => this.topics.set(topics),
        error: () => this.topicsError.set(true),
      });
  }

  protected search(): void {
    if (this.form.invalid) return;
    const { q, topic } = this.form.getRawValue();
    this.filters = { q: q.trim(), topic };
    this.items.set([]);
    this.cursor.set(null);
    this.load(null);
  }

  protected load(cursor: number | null): void {
    this.request?.unsubscribe();
    this.loading.set(true);
    this.error.set('');
    this.request = this.api
      .list(this.filters.q, this.filters.topic, cursor)
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
