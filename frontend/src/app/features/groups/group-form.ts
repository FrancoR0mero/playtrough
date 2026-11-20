import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { catchError, map, of, startWith, Subject, switchMap } from 'rxjs';
import { normalizePlainText } from '../../shared/plain-text';
import { GroupPrivacy, GroupProfile, GroupsApi } from './groups-api';

@Component({
  selector: 'app-group-form',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './group-form.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GroupFormComponent implements OnInit {
  private readonly api = inject(GroupsApi);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly route = inject(ActivatedRoute);
  private readonly reload = new Subject<void>();
  private readonly group = signal<GroupProfile | null>(null);

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
  private readonly topics = signal<string[]>([]);
  protected readonly availableTopics = computed(() => {
    const topic = this.group()?.topic;
    return topic && !this.topics().includes(topic) ? [...this.topics(), topic] : this.topics();
  });
  protected readonly topicsLoading = signal(false);
  protected readonly topicsError = signal(false);
  protected readonly saving = signal(false);
  protected readonly error = signal('');
  protected readonly editId = signal<string | null>(null);
  protected readonly groupLoading = signal(false);
  protected readonly groupError = signal('');
  protected readonly conflict = signal(false);

  ngOnInit(): void {
    this.loadTopics();
    this.route.paramMap
      .pipe(
        switchMap((params) => {
          const id = params.get('id');
          this.editId.set(id);
          this.saving.set(false);
          if (id === null) {
            this.form.reset();
            return of(null);
          }
          return this.reload.pipe(
            startWith(undefined),
            switchMap(() => {
              this.group.set(null);
              this.groupLoading.set(true);
              this.groupError.set('');
              this.error.set('');
              this.conflict.set(false);
              return this.api.get(id).pipe(
                catchError((response: HttpErrorResponse) => {
                  this.groupError.set(
                    response.status === 404
                      ? 'El grupo no está disponible.'
                      : 'No pudimos cargar el grupo.',
                  );
                  return of(null);
                }),
              );
            }),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((group) => {
        this.groupLoading.set(false);
        if (!group) return;
        if (group.membership !== 'owner' || !group.version) {
          this.groupError.set('Solo el propietario puede editar este grupo.');
          return;
        }
        this.form.reset({
          name: group.name,
          description: group.description,
          topic: group.topic,
          privacy: group.privacy,
        });
        this.group.set(group);
      });
  }

  protected reloadGroup(): void {
    this.reload.next();
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

  protected save(): void {
    const group = this.group();
    const editId = this.editId();
    if (
      this.saving() ||
      this.topicsLoading() ||
      this.topicsError() ||
      this.conflict() ||
      (editId !== null && !group?.version)
    )
      return;
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
    const request = group?.version
      ? this.api.update(group.id, draft, group.version).pipe(map(() => group.id))
      : this.api.create(draft).pipe(map((created) => created.id));
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (id) => {
        if (this.editId() === editId)
          void this.router.navigate(['/groups', id], { replaceUrl: group !== null });
      },
      error: (response: HttpErrorResponse) => {
        if (this.editId() !== editId) return;
        this.saving.set(false);
        if (response.status === 412) {
          this.conflict.set(true);
          this.error.set(
            'El grupo cambió. Si cargás la versión actual, se descartarán tus cambios sin guardar.',
          );
          return;
        }
        const fields = response.status === 400 ? response.error?.errors : undefined;
        const first =
          fields &&
          Object.values(fields)
            .flat()
            .find((value) => typeof value === 'string');
        this.error.set(
          typeof first === 'string'
            ? first
            : group
              ? 'No pudimos guardar los cambios. Volvé a intentarlo.'
              : 'No pudimos crear el grupo. Volvé a intentarlo.',
        );
      },
    });
  }
}
