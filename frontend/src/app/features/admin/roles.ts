import { Dialog, DialogRef } from '@angular/cdk/dialog';
import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import {
  BehaviorSubject,
  EMPTY,
  catchError,
  debounceTime,
  distinctUntilChanged,
  finalize,
  map,
  switchMap,
} from 'rxjs';
import { APP_ROLES, AppRole } from '../../core/authorization/roles';
import { ProblemDetails } from '../auth/models/register';
import { AuthService } from '../auth/services/auth';
import { ConfirmRoleChangeComponent } from './confirm-role-change';
import {
  AdminUser,
  PagedResponse,
  RoleChangeConfirmation,
  UserListQuery,
} from './models/admin-users';
import { AdminUsersService } from './services/admin-users';

interface Notice {
  kind: 'success' | 'error';
  text: string;
  retry?: RoleChangeConfirmation;
}

@Component({
  selector: 'app-admin-roles',
  imports: [ReactiveFormsModule],
  templateUrl: './roles.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminRolesComponent implements OnInit {
  protected readonly auth = inject(AuthService);
  private readonly admin = inject(AdminUsersService);
  private readonly dialogs = inject(Dialog);
  private readonly destroyRef = inject(DestroyRef);
  private readonly queries = new BehaviorSubject<UserListQuery>({ page: 1, pageSize: 20 });
  private readonly controls = new Map<number, FormControl<AppRole>>();
  private confirmation: DialogRef<boolean, ConfirmRoleChangeComponent> | null = null;
  protected readonly roles = APP_ROLES;
  protected readonly search = new FormControl('', { nonNullable: true });
  protected readonly roleFilter = new FormControl<AppRole | ''>('', { nonNullable: true });
  protected readonly result = signal<PagedResponse<AdminUser> | null>(null);
  protected readonly loading = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly notice = signal<Notice | null>(null);
  protected readonly saving = signal<ReadonlySet<number>>(new Set());
  protected readonly totalPages = computed(() =>
    Math.max(1, Math.ceil((this.result()?.total ?? 0) / (this.result()?.pageSize ?? 20))),
  );

  constructor() {
    this.destroyRef.onDestroy(() => this.confirmation?.close());
  }

  ngOnInit(): void {
    this.queries
      .pipe(
        switchMap((query) => {
          this.loading.set(true);
          this.loadError.set(null);
          return this.admin.listUsers(query).pipe(
            catchError((error: unknown) => {
              if (!(error instanceof HttpErrorResponse && error.status === 401))
                this.loadError.set(
                  error instanceof HttpErrorResponse && error.status === 403
                    ? 'No tenés permisos para esta acción'
                    : 'No pudimos cargar los usuarios. Intentá de nuevo.',
                );
              return EMPTY;
            }),
            finalize(() => this.loading.set(false)),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((response) => {
        const lastPage = Math.max(1, Math.ceil(response.total / response.pageSize));
        if (response.page > lastPage) {
          this.reload(lastPage);
          return;
        }
        this.result.set(response);
        const visibleIds = new Set(response.items.map((user) => user.id));
        for (const id of this.controls.keys()) if (!visibleIds.has(id)) this.controls.delete(id);
        for (const user of response.items)
          this.controls.get(user.id)?.setValue(user.role, { emitEvent: false });
      });
    this.search.valueChanges
      .pipe(
        map((value) => value.trim()),
        debounceTime(300),
        distinctUntilChanged(),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(() => this.reload(1));
    this.roleFilter.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.reload(1));
  }

  protected reload(page = this.queries.value.page): void {
    this.queries.next({
      page,
      pageSize: 20,
      search: this.search.value.trim(),
      role: this.roleFilter.value || undefined,
    });
  }

  protected goToPage(page: number): void {
    if (!this.loading() && page >= 1 && page <= this.totalPages()) this.reload(page);
  }

  protected roleControl(user: AdminUser): FormControl<AppRole> {
    let control = this.controls.get(user.id);
    if (!control) {
      control = new FormControl(user.role, { nonNullable: true });
      this.controls.set(user.id, control);
    }
    const disabled =
      user.id === this.auth.currentUser()?.id || this.saving().has(user.id) || this.loading();
    if (disabled !== control.disabled) {
      if (disabled) control.disable({ emitEvent: false });
      else control.enable({ emitEvent: false });
    }
    return control;
  }

  protected chooseRole(user: AdminUser): void {
    const control = this.roleControl(user);
    const role = control.value;
    control.setValue(user.role, { emitEvent: false });
    if (control.disabled || role === user.role || this.confirmation) return;
    this.confirmChange({ user, role });
  }

  private confirmChange(change: RoleChangeConfirmation): void {
    if (
      this.confirmation ||
      change.user.id === this.auth.currentUser()?.id ||
      this.saving().has(change.user.id)
    )
      return;
    this.confirmation = this.dialogs.open<
      boolean,
      RoleChangeConfirmation,
      ConfirmRoleChangeComponent
    >(ConfirmRoleChangeComponent, {
      data: change,
      role: 'dialog',
      ariaModal: true,
      ariaLabelledBy: 'role-change-title',
      ariaDescribedBy: 'role-change-description',
      autoFocus: '[data-action="cancel"]',
      restoreFocus: true,
      width: 'calc(100vw - 2rem)',
      maxWidth: '32rem',
    });
    this.confirmation.closed.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((confirmed) => {
      this.confirmation = null;
      if (confirmed) this.saveRole(change);
    });
  }

  protected retryChange(): void {
    const retry = this.notice()?.retry;
    if (retry) this.confirmChange(retry);
  }

  private saveRole(change: RoleChangeConfirmation): void {
    this.notice.set(null);
    this.saving.update((ids) => new Set([...ids, change.user.id]));
    this.admin
      .changeRole(change.user.id, change.role)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() =>
          this.saving.update((ids) => {
            const next = new Set(ids);
            next.delete(change.user.id);
            return next;
          }),
        ),
      )
      .subscribe({
        next: (updated) => {
          this.result.update(
            (page) =>
              page && {
                ...page,
                items: page.items.map((user) => (user.id === updated.id ? updated : user)),
              },
          );
          this.controls.get(updated.id)?.setValue(updated.role, { emitEvent: false });
          this.notice.set({
            kind: 'success',
            text: `El rol de ${updated.username} se actualizó a ${updated.role}.`,
          });
          if (this.loading() || (this.roleFilter.value && this.roleFilter.value !== updated.role))
            this.reload();
        },
        error: (error: unknown) => {
          if (error instanceof HttpErrorResponse && error.status === 401) return; // The interceptor owns session expiry.
          const status = error instanceof HttpErrorResponse ? error.status : 0;
          const problem =
            error instanceof HttpErrorResponse ? (error.error as ProblemDetails | null) : null;
          const code = problem?.code ?? problem?.extensions?.code;
          let text = 'No pudimos cambiar el rol. Intentá de nuevo.';
          if (status === 403) text = 'No tenés permisos para esta acción';
          else if (status === 404) text = 'El usuario ya no existe. Actualizamos la lista.';
          else if (status === 400) {
            if (code === 'cannot_change_own_role') text = 'No podés cambiar tu propio rol';
            if (code === 'last_admin') text = 'No se puede quitar el rol al último administrador';
            if (code === 'invalid_role') text = 'El rol seleccionado no es válido.';
          }
          this.notice.set({
            kind: 'error',
            text,
            ...(status === 0 || status >= 500 ? { retry: change } : {}),
          });
          if (status === 400 || status === 403 || status === 404) this.reload();
        },
      });
  }
}
