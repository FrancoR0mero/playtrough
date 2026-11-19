import { HttpErrorResponse } from '@angular/common/http';
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
import { Router, RouterLink } from '@angular/router';
import { normalizePlainText } from '../../shared/plain-text';
import { GroupPrivacy, GroupsApi } from './groups-api';

@Component({
  selector: 'app-group-create',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './group-create.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GroupCreateComponent implements OnInit {
  private readonly api = inject(GroupsApi);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly form = new FormGroup({
    name: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(80)],
    }),
    description: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(500)],
    }),
    topic: new FormControl('', { nonNullable: true, validators: Validators.required }),
    privacy: new FormControl<GroupPrivacy>('public', { nonNullable: true }),
  });
  protected readonly topics = signal<string[]>([]);
  protected readonly topicsLoading = signal(false);
  protected readonly topicsError = signal(false);
  protected readonly saving = signal(false);
  protected readonly error = signal('');

  ngOnInit(): void {
    this.loadTopics();
  }

  protected loadTopics(): void {
    this.topicsLoading.set(true);
    this.topicsError.set(false);
    this.api
      .topics()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (topics) => {
          this.topics.set(topics);
          this.topicsLoading.set(false);
        },
        error: () => {
          this.topicsError.set(true);
          this.topicsLoading.set(false);
        },
      });
  }

  protected create(): void {
    if (this.saving() || this.topicsLoading() || this.topicsError()) return;
    this.error.set('');
    const { name, description, topic, privacy } = this.form.getRawValue();
    const draft = {
      name: normalizePlainText(name),
      description: normalizePlainText(description),
      topic,
      privacy,
    };
    this.form.patchValue({ name: draft.name, description: draft.description });
    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    this.saving.set(true);
    this.api
      .create(draft)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (group) => void this.router.navigate(['/groups', group.id]),
        error: (response: HttpErrorResponse) => {
          this.saving.set(false);
          const fields = response.status === 400 ? response.error?.errors : undefined;
          const first =
            fields &&
            Object.values(fields)
              .flat()
              .find((value) => typeof value === 'string');
          this.error.set(
            typeof first === 'string' ? first : 'No pudimos crear el grupo. Volvé a intentarlo.',
          );
        },
      });
  }
}
