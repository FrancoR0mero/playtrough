import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { jwtInterceptor } from '../../core/interceptors/jwt-interceptor';
import { ReactionsService } from '../reactions/services/reactions';
import { EntryCommentsComponent } from './entry-comments';
import { CommentDto } from './models/comment';

const base = `${environment.apiUrl}/timeline`;
const listUrl = `${base}/entries/42/comments`;

function comment(id: number, overrides: Partial<CommentDto> = {}): CommentDto {
  return {
    id,
    author: { username: 'bea', avatarUrl: null },
    text: 'Comentario original',
    createdAt: '2026-03-12T12:00:00Z',
    editedAt: null,
    likeCount: 0,
    dislikeCount: 0,
    myReaction: null,
    isMine: false,
    canDelete: false,
    ...overrides,
  };
}

describe('Entry comments', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-03-12T14:00:00Z'));
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([jwtInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
    vi.useRealTimers();
  });

  function setup() {
    const fixture = TestBed.createComponent(EntryCommentsComponent);
    const root = fixture.nativeElement as HTMLElement;
    const http = TestBed.inject(HttpTestingController);
    const counts = vi.fn();
    const missing = vi.fn();
    fixture.componentRef.setInput('entryId', 42);
    fixture.componentRef.setInput('count', 0);
    fixture.componentInstance.countChanged.subscribe((count) => {
      counts(count);
      fixture.componentRef.setInput('count', count);
    });
    fixture.componentInstance.unavailable.subscribe(missing);
    fixture.detectChanges();
    const page = (
      items: CommentDto[],
      cursor: string | null = null,
      nextCursor: string | null = null,
      commentCount = items.length,
    ) => {
      const url = `${listUrl}?${cursor ? 'cursor=' + cursor + '&' : ''}limit=20`;
      http
        .expectOne({ method: 'GET', url })
        .flush({ items, commentCount, nextCursor, hasMore: !!nextCursor });
      fixture.detectChanges();
    };
    const draft = () => root.querySelector<HTMLTextAreaElement>('#entry-comment-draft')!;
    const input = (node: HTMLTextAreaElement, text: string) => {
      node.value = text;
      node.dispatchEvent(new Event('input'));
      fixture.detectChanges();
    };
    const button = (text: string, container: ParentNode = root) =>
      Array.from(container.querySelectorAll('button')).find(
        (element) => element.textContent?.trim() === text,
      )!;
    const row = (id: number) => root.querySelector<HTMLElement>(`[data-comment-id="${id}"]`)!;
    const submit = (container: ParentNode = root) => {
      container
        .querySelector('form')!
        .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    };
    const act = (id: number, action: string) => {
      row(id).querySelector<HTMLButtonElement>('[data-comment-menu]')!.click();
      fixture.detectChanges();
      button(action, row(id)).click();
      fixture.detectChanges();
    };
    const key = (node: HTMLElement, value: string, options: KeyboardEventInit = {}) => {
      node.dispatchEvent(new KeyboardEvent('keydown', { key: value, bubbles: true, ...options }));
      fixture.detectChanges();
    };
    return {
      fixture,
      root,
      http,
      counts,
      missing,
      page,
      draft,
      input,
      button,
      row,
      act,
      key,
      submit,
    };
  }

  it('counts normalized Unicode, submits with a shortcut, and prepends the new row', () => {
    const f = setup();
    expect(f.root.querySelector('[aria-label="Cargando comentarios"]')).not.toBeNull();
    f.page([comment(1)]);
    expect(f.button('Comentar').disabled).toBe(true);
    f.input(f.draft(), ' \t\n ');
    expect(f.button('Comentar').disabled).toBe(true);
    f.input(f.draft(), '😀'.repeat(501));
    expect(f.root.textContent).toContain('501 / 500');
    expect(f.button('Comentar').disabled).toBe(true);
    const text = '😀'.repeat(500);
    f.input(f.draft(), ' ' + text + ' ');
    expect(f.root.textContent).toContain('500 / 500');
    f.key(f.draft(), 'Enter');
    f.http.expectNone({ method: 'POST', url: listUrl });
    f.key(f.draft(), 'Enter', { ctrlKey: true });
    expect(f.draft().disabled).toBe(true);
    const request = f.http.expectOne({ method: 'POST', url: listUrl });
    expect(request.request.body).toEqual({ text });
    request.flush(
      { comment: comment(2, { text, isMine: true, canDelete: true }), commentCount: 2 },
      { status: 201, statusText: 'Created' },
    );
    f.fixture.detectChanges();
    expect(f.root.querySelector('li')?.getAttribute('data-comment-id')).toBe('2');
    expect(f.draft().value).toBe('');
    expect(f.draft().disabled).toBe(false);
    expect(f.counts).toHaveBeenLastCalledWith(2);
    expect(f.root.querySelector('[role="status"]')?.textContent).toContain('Comentario publicado');
  });

  it('shows an empty availability message when the total includes filtered comments', () => {
    const f = setup();
    f.page([], null, null, 2);
    expect(f.root.textContent).toContain('No hay comentarios disponibles');
    expect(f.root.textContent).not.toContain('Sé el primero');
  });

  it.each([
    [400, 'invalid_comment', 'hasta 500'],
    [409, 'duplicate_comment', 'Ya publicaste este comentario hace un momento'],
    [429, 'rate_limited', '2 segundos'],
    [503, 'unavailable', 'Volvé a intentarlo'],
  ] as const)('preserves the draft after an HTTP %i', (status, code, message) => {
    const f = setup();
    f.page([]);
    expect(f.root.textContent).toContain('Todavía no hay comentarios. Sé el primero');
    f.input(f.draft(), 'Mi borrador');
    f.key(f.draft(), 'Enter', { metaKey: true });
    f.http
      .expectOne({ method: 'POST', url: listUrl })
      .flush(
        { extensions: { code } },
        { status, statusText: 'Error', headers: { 'Retry-After': '2' } },
      );
    f.fixture.detectChanges();
    expect(f.draft().value).toBe('Mi borrador');
    expect(f.draft().disabled).toBe(false);
    expect(f.root.querySelector('[role="alert"]')?.textContent).toContain(message);
    if (status === 429) {
      expect(f.button('Comentar').disabled).toBe(true);
      vi.advanceTimersByTime(2000);
      f.fixture.detectChanges();
      expect(f.button('Comentar').disabled).toBe(false);
    }
  });

  it('signals that the entry disappeared when publishing returns 404', () => {
    const f = setup();
    f.page([]);
    f.input(f.draft(), 'Mi borrador');
    f.submit();
    f.http
      .expectOne({ method: 'POST', url: listUrl })
      .flush({ code: 'entry_not_found' }, { status: 404, statusText: 'Not Found' });
    expect(f.missing).toHaveBeenCalledOnce();
    expect(f.draft().value).toBe('Mi borrador');
  });

  it('paginates once, deduplicates, retries, and renders time and text literally', () => {
    const f = setup();
    const literal = '<script>alert(1)</script>\n<b>hola</b>';
    const first = comment(3, { text: literal, editedAt: '2026-03-12T13:00:00Z' });
    f.page([first], null, 'v1.more');
    expect(f.row(3).querySelector('[data-comment-text]')?.textContent?.trim()).toBe(literal);
    expect(
      f.row(3).querySelector('[data-comment-text]')?.classList.contains('whitespace-pre-line'),
    ).toBe(true);
    expect(f.row(3).querySelector('script, b')).toBeNull();
    expect(f.row(3).querySelector('time')?.textContent).toBe('hace 2 horas');
    expect(f.row(3).querySelector('time')?.getAttribute('datetime')).toBe(
      '2026-03-12T12:00:00.000Z',
    );
    expect(f.row(3).querySelector('time')?.title).toContain('2026');
    expect(f.row(3).textContent).toContain('(editado)');
    expect(f.row(3).querySelector('a[href="/profile/bea"]')).not.toBeNull();
    const more = f.button('Cargar más');
    more.click();
    more.click();
    f.http
      .expectOne(`${listUrl}?cursor=v1.more&limit=20`)
      .flush({}, { status: 503, statusText: 'Error' });
    f.fixture.detectChanges();
    expect(f.root.textContent).toContain('No pudimos cargar los comentarios');
    f.button('Reintentar').click();
    f.page([first, comment(2)], 'v1.more');
    expect(
      [...f.root.querySelectorAll('li')].map((row) => row.getAttribute('data-comment-id')),
    ).toEqual(['3', '2']);
    expect(f.button('Cargar más')).toBeUndefined();
  });

  it('honors menu permissions and keyboard focus, edits in place and preserves an invalid edit', () => {
    const f = setup();
    f.page([
      comment(3, { isMine: true, canDelete: true }),
      comment(2, { canDelete: true }),
      comment(1),
    ]);
    expect(f.row(1).querySelector('[data-comment-menu]')).toBeNull();
    f.row(2).querySelector<HTMLButtonElement>('[data-comment-menu]')!.click();
    f.fixture.detectChanges();
    expect(f.button('Editar', f.row(2))).toBeUndefined();
    expect(f.button('Eliminar', f.row(2))).toBeDefined();
    const trigger = f.row(3).querySelector<HTMLButtonElement>('[data-comment-menu]')!;
    f.key(trigger, 'ArrowDown');
    expect(document.activeElement).toBe(f.button('Editar', f.row(3)));
    f.key(document.activeElement as HTMLElement, 'ArrowDown');
    expect(document.activeElement).toBe(f.button('Eliminar', f.row(3)));
    f.key(document.activeElement as HTMLElement, 'Escape');
    expect(document.activeElement).toBe(trigger);

    f.act(3, 'Editar');
    let editor = f.row(3).querySelector<HTMLTextAreaElement>('[data-comment-edit]')!;
    expect(document.activeElement).toBe(editor);
    expect(f.button('Guardar', f.row(3)).disabled).toBe(true);
    f.key(editor, 'Escape');
    expect(f.row(3).querySelector('textarea')).toBeNull();
    f.act(3, 'Editar');
    editor = f.row(3).querySelector<HTMLTextAreaElement>('[data-comment-edit]')!;
    f.input(editor, 'Nueva versión');
    f.submit(f.row(3));
    const editUrl = `${base}/comments/3`;
    f.http
      .expectOne({ method: 'PUT', url: editUrl })
      .flush({ code: 'invalid_comment' }, { status: 400, statusText: 'Bad Request' });
    f.fixture.detectChanges();
    expect(editor.value).toBe('Nueva versión');
    expect(f.row(3).querySelector('[role="alert"]')).not.toBeNull();
    f.submit(f.row(3));
    const saved = f.http.expectOne({ method: 'PUT', url: editUrl });
    expect(saved.request.body).toEqual({ text: 'Nueva versión' });
    saved.flush(
      comment(3, {
        text: 'Nueva versión',
        isMine: true,
        canDelete: true,
        editedAt: '2026-03-12T14:00:00Z',
      }),
    );
    f.fixture.detectChanges();
    expect(f.row(3).textContent).toContain('Nueva versión');
    expect(f.row(3).textContent).toContain('(editado)');
  });

  it('confirms inline, removes the comment and focuses the next row or composer', () => {
    const f = setup();
    f.page([comment(2, { canDelete: true }), comment(1, { isMine: true, canDelete: true })]);
    f.act(2, 'Eliminar');
    expect(f.row(2).textContent).toContain('¿Eliminar este comentario?');
    f.button('Cancelar', f.row(2)).click();
    f.fixture.detectChanges();
    f.http.expectNone(`${base}/comments/2`);
    f.act(2, 'Eliminar');
    f.button('Eliminar', f.row(2)).click();
    f.http.expectOne({ method: 'DELETE', url: `${base}/comments/2` }).flush({ commentCount: 1 });
    f.fixture.detectChanges();
    expect(f.row(2)).toBeNull();
    expect(f.counts).toHaveBeenLastCalledWith(1);
    expect(document.activeElement).toBe(f.row(1));
    f.act(1, 'Eliminar');
    f.button('Eliminar', f.row(1)).click();
    f.http.expectOne({ method: 'DELETE', url: `${base}/comments/1` }).flush({ commentCount: 0 });
    f.fixture.detectChanges();
    expect(document.activeElement).toBe(f.draft());
  });

  it.each(['Editar', 'Eliminar'])(
    'removes an unavailable comment after %s and reloads the count',
    (action) => {
      const f = setup();
      f.page([comment(1, { isMine: true, canDelete: true })]);
      f.act(1, action);
      if (action === 'Editar') f.input(f.row(1).querySelector('textarea')!, 'Ya no existe');
      if (action === 'Editar') f.submit(f.row(1));
      else f.button('Eliminar', f.row(1)).click();
      f.http
        .expectOne(`${base}/comments/1`)
        .flush({ code: 'comment_not_found' }, { status: 404, statusText: 'Not Found' });
      f.page([]);
      expect(f.row(1)).toBeNull();
      expect(f.counts).toHaveBeenLastCalledWith(0);
    },
  );

  it('reacts to other comments, keeps own counts disabled and synchronizes only the chosen comment', () => {
    const f = setup();
    f.page([
      comment(1, { likeCount: 3, dislikeCount: 1, canDelete: true }),
      comment(2, { likeCount: 7, dislikeCount: 4, isMine: true, canDelete: true }),
    ]);
    const saved = vi.fn();
    const changes = TestBed.inject(ReactionsService).changes$.subscribe(saved);
    const like = (id: number) =>
      f.row(id).querySelector<HTMLButtonElement>('[data-reaction="like"]')!;
    const url = `${base}/comments/1/reaction`;
    expect(like(2).disabled).toBe(true);
    expect(like(2).getAttribute('aria-label')).toBe('Me gusta (7)');
    like(2).click();
    f.http.expectNone(`${base}/comments/2/reaction`);
    like(1).click();
    f.fixture.detectChanges();
    expect(like(1).getAttribute('aria-label')).toBe('Me gusta (4)');
    f.http
      .expectOne({ method: 'PUT', url })
      .flush({ myReaction: 'like', likeCount: 5, dislikeCount: 1 });
    f.fixture.detectChanges();
    expect(like(1).getAttribute('aria-label')).toBe('Me gusta (5)');
    expect(like(2).getAttribute('aria-label')).toBe('Me gusta (7)');
    expect(saved).toHaveBeenLastCalledWith(
      expect.objectContaining({
        kind: 'saved',
        target: { type: 'comment', id: 1 },
        summary: { myReaction: 'like', likeCount: 5, dislikeCount: 1 },
      }),
    );
    changes.unsubscribe();
    f.act(1, 'Eliminar');
    f.button('Cancelar', f.row(1)).click();
    f.fixture.detectChanges();
    expect(like(1).getAttribute('aria-pressed')).toBe('true');

    like(1).click();
    f.http
      .expectOne({ method: 'DELETE', url })
      .flush(
        { code: 'rate_limited' },
        { status: 429, statusText: 'Too Many Requests', headers: { 'Retry-After': '2' } },
      );
    f.fixture.detectChanges();
    expect(like(1).getAttribute('aria-pressed')).toBe('true');
    expect(f.row(1).querySelector('[role="alert"]')?.textContent).toContain('2 segundos');
    vi.advanceTimersByTime(2000);
    f.fixture.detectChanges();
    like(1).click();
    f.http
      .expectOne({ method: 'DELETE', url })
      .flush({ code: 'comment_not_found' }, { status: 404, statusText: 'Not Found' });
    f.fixture.detectChanges();
    expect(f.row(1)).toBeNull();
    expect(f.row(2)).not.toBeNull();
    expect(f.root.querySelector('[role="alert"]')?.textContent).toContain(
      'Este comentario ya no está disponible',
    );
    expect(f.counts).toHaveBeenLastCalledWith(1);
  });
});
