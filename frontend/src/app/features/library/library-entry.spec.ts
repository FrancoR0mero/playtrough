import { HttpErrorResponse, HttpHeaders, provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { environment } from '../../../environments/environment';
import { RateLimitError } from '../../core/http/rate-limit-error';
import { localCalendarDate } from './library-date';
import { LibraryEntryComponent } from './library-entry';
import { LibraryEntryDto } from './models/library';
import { LibraryService } from './services/library';

const ENTRY: LibraryEntryDto = {
  gameId: 17,
  externalId: 4200,
  name: 'Portal 2',
  slug: 'portal-2',
  coverUrl: null,
  releaseYear: 2011,
  status: 'playing',
  statusChangedOn: '2026-10-27',
  addedAt: '2026-10-28T02:30:00Z',
};

describe('Library section inside the game detail modal', () => {
  let fixture: ComponentFixture<LibraryEntryComponent>;
  let http: HttpTestingController;
  let service: LibraryService;
  let changed = vi.fn();
  const url = `${environment.apiUrl}/library`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [LibraryEntryComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(LibraryService);
    changed = vi.fn();
    service.changes$.subscribe(changed);
    fixture = TestBed.createComponent(LibraryEntryComponent);
    fixture.componentRef.setInput('gameId', 17);
    fixture.componentRef.setInput('gameName', 'Portal 2');
    fixture.detectChanges();
  });

  afterEach(() => {
    try {
      http.verify();
    } finally {
      fixture.destroy();
      TestBed.resetTestingModule();
      vi.restoreAllMocks();
      vi.useRealTimers();
    }
  });

  function element<T extends Element = HTMLElement>(selector: string): T | null {
    return fixture.nativeElement.querySelector(selector);
  }
  function button(action: string): HTMLButtonElement {
    return element<HTMLButtonElement>(`[data-action="${action}"]`)!;
  }
  function click(action: string): void {
    button(action).click();
    fixture.detectChanges();
  }
  function fail(request: TestRequest, code: string, status = 400, extra = {}): void {
    request.flush({ extensions: { code, ...extra } }, { status, statusText: 'Error' });
    fixture.detectChanges();
  }
  function missing(): void {
    fail(http.expectOne(`${url}/games/17`), 'not_in_library', 404);
  }
  function existing(entry = ENTRY): void {
    http.expectOne(`${url}/games/17`).flush(entry);
    fixture.detectChanges();
  }
  function chooseDate(value: string): void {
    const input = element<HTMLInputElement>('input[type="date"]')!;
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
  }

  it('shows a skeleton then treats only 404 not_in_library as a normal absence', () => {
    expect(element('[data-library-skeleton]')).not.toBeNull();
    expect(element('[role="status"]')?.textContent).toContain('Cargando');
    missing();
    expect(element('[data-library-skeleton]')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('no está en tu biblioteca');
    expect(element('[role="alert"]')).toBeNull();
    expect(button('save-library').disabled).toBe(true);
    expect(changed).not.toHaveBeenCalled();
  });

  it('shows current state, calendar date and the selected radio', () => {
    existing();
    expect(element('[data-library-status]')?.textContent).toBe('Jugando');
    expect(fixture.nativeElement.textContent).toContain('desde el');
    expect(element('time')?.textContent).toBe('27/10/2026');
    expect(element('time')?.getAttribute('datetime')).toBe('2026-10-27');
    expect(button('choose-playing').getAttribute('aria-checked')).toBe('true');
    expect(button('save-library').textContent).toContain('Actualizar estado');
  });

  it.each([false, true])(
    'adds with explicit date enabled=%s, locks controls and emits exactly once',
    (withDate) => {
      missing();
      click('choose-backlog');
      expect(button('save-library').disabled).toBe(false);
      if (withDate) {
        click('toggle-date');
        expect(element<HTMLInputElement>('input')?.value).toBe(localCalendarDate());
        expect(element('input')?.getAttribute('max')).toBe(localCalendarDate());
        chooseDate('2026-01-02');
      }
      click('save-library');
      button('save-library').click();
      expect(button('save-library').disabled).toBe(true);
      expect(element<HTMLFieldSetElement>('fieldset')?.disabled).toBe(true);
      expect(button('choose-playing').disabled).toBe(true);
      expect(element('[role="status"]')?.textContent).toContain('Guardando');
      const request = http.expectOne(url);
      expect(request.request.body).toEqual({
        gameId: 17,
        status: 'backlog',
        ...(withDate ? { occurredOn: '2026-01-02' } : {}),
      });
      const entry = {
        ...ENTRY,
        status: 'backlog' as const,
        statusChangedOn: withDate ? '2026-01-02' : ENTRY.statusChangedOn,
      };
      request.flush(entry, { status: 201, statusText: 'Created' });
      fixture.detectChanges();
      expect(element('[data-library-status]')?.textContent).toBe('Backlog');
      expect(element('[role="status"]')?.textContent).toContain('Agregado a tu biblioteca');
      expect(button('save-library').disabled).toBe(false);
      expect(changed).toHaveBeenCalledExactlyOnceWith({ type: 'added', gameId: 17, entry });
    },
  );

  it('does not send a hidden date, even after the user edited it', () => {
    missing();
    click('choose-playing');
    click('toggle-date');
    chooseDate('2026-01-02');
    click('toggle-date');
    click('save-library');
    const request = http.expectOne(url);
    expect(request.request.body).toEqual({ gameId: 17, status: 'playing' });
    request.flush(ENTRY);
  });

  it.each(['', '2999-01-01'])('rejects an empty or future explicit date: %s', (value) => {
    missing();
    click('choose-playing');
    click('toggle-date');
    chooseDate(value);
    click('save-library');
    expect(element('[role="alert"]')?.textContent).toContain('Revisá la fecha');
    http.expectNone(url);
  });

  it.each([false, true])('updates state with explicit date enabled=%s', (withDate) => {
    existing();
    click('choose-completed');
    if (withDate) {
      click('toggle-date');
      chooseDate('2026-10-27');
    }
    click('save-library');
    const request = http.expectOne(`${url}/17/status`);
    expect(request.request.body).toEqual({
      status: 'completed',
      ...(withDate ? { occurredOn: '2026-10-27' } : {}),
    });
    const updated = { ...ENTRY, status: 'completed' as const };
    request.flush(updated);
    fixture.detectChanges();
    expect(element('[data-library-status]')?.textContent).toBe('Completado');
    expect(element('[role="status"]')?.textContent).toContain('Estado actualizado');
    expect(changed).toHaveBeenCalledExactlyOnceWith({
      type: 'status-changed',
      gameId: 17,
      entry: updated,
    });
  });

  it('allows submitting the current status to update its date', () => {
    existing();
    click('toggle-date');
    chooseDate('2026-10-27');
    click('save-library');
    const request = http.expectOne(`${url}/17/status`);
    expect(request.request.body).toEqual({ status: 'playing', occurredOn: '2026-10-27' });
    request.flush(ENTRY);
  });

  it('confirms removal inline, cancel makes no request, then removes and announces success', () => {
    existing();
    click('remove-library');
    expect(element('[role="dialog"]')).toBeNull();
    expect(element('[role="group"]')?.textContent).toContain('¿Quitar Portal 2 de tu biblioteca?');
    expect(document.activeElement).toBe(button('cancel-remove'));
    click('cancel-remove');
    expect(element('[role="group"]')).toBeNull();
    expect(document.activeElement).toBe(button('remove-library'));
    http.expectNone(`${url}/17`);
    click('remove-library');
    click('confirm-remove');
    button('confirm-remove').click();
    expect(button('cancel-remove').disabled).toBe(true);
    expect(button('save-library').disabled).toBe(true);
    http.expectOne(`${url}/17`).flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();
    expect(element('[data-library-status]')).toBeNull();
    expect(element('[role="group"]')).toBeNull();
    expect(element('[role="status"]')?.textContent).toContain('Quitado de tu biblioteca');
    expect(document.activeElement).toBe(button('choose-playing'));
    expect(changed).toHaveBeenCalledExactlyOnceWith({ type: 'removed', gameId: 17 });
  });

  it.each([
    ['invalid_status', 'Elegí un estado válido.'],
    ['invalid_date', 'Revisá la fecha'],
    ['date_before_previous_event', 'La fecha no puede ser anterior al último cambio (26/10/2026)'],
    ['game_not_found', 'No encontramos este juego.'],
    ['unknown_code', 'No pudimos actualizar tu biblioteca.'],
  ])('shows an accessible error for %s and keeps the form editable', (code, message) => {
    existing();
    click('choose-beaten');
    click('save-library');
    fail(http.expectOne(`${url}/17/status`), code, 400, { minDate: '2026-10-26' });
    expect(element('[role="alert"]')?.textContent).toContain(message);
    expect(button('save-library').disabled).toBe(false);
    expect(button('choose-beaten').getAttribute('aria-checked')).toBe('true');
    expect(changed).not.toHaveBeenCalled();
  });

  it.each(['already_in_library', 'concurrent_modification'])(
    'reloads the real entry after %s and preserves the explanation',
    (code) => {
      missing();
      click('choose-backlog');
      click('save-library');
      fail(http.expectOne(url), code, 409);
      expect(element('[data-library-skeleton]')).not.toBeNull();
      existing({ ...ENTRY, status: 'beaten' });
      expect(element('[data-library-status]')?.textContent).toBe('Terminado');
      expect(element('[role="alert"]')?.textContent).toContain(
        code === 'already_in_library' ? 'ya está en tu biblioteca' : 'volvé a intentar',
      );
      expect(changed).not.toHaveBeenCalled();
    },
  );

  it.each(['status', 'remove'])('returns to absence after not_in_library during %s', (action) => {
    existing();
    if (action === 'status') click('save-library');
    else {
      click('remove-library');
      click('confirm-remove');
    }
    fail(http.expectOne(`${url}/17${action === 'status' ? '/status' : ''}`), 'not_in_library', 404);
    expect(fixture.nativeElement.textContent).toContain('no está en tu biblioteca');
    expect(element('[role="alert"]')?.textContent).toContain('ya no está');
    expect(element('[data-library-status]')).toBeNull();
    expect(changed).not.toHaveBeenCalled();
  });

  it('retries a failed initial load without issuing duplicate requests', () => {
    http.expectOne(`${url}/games/17`).error(new ProgressEvent('error'));
    fixture.detectChanges();
    expect(element('[role="alert"]')?.textContent).toContain('Revisá tu conexión');
    click('retry-library');
    existing();
    expect(element('[role="alert"]')).toBeNull();
  });

  it('does not treat a different 404 as an absent entry', () => {
    fail(http.expectOne(`${url}/games/17`), 'game_not_found', 404);
    expect(element('[role="alert"]')?.textContent).toContain('No encontramos este juego');
    expect(button('save-library')).toBeNull();
    expect(button('retry-library')).not.toBeNull();
  });

  it('keeps a failed conflict reload retryable and can resolve absence', () => {
    existing();
    click('save-library');
    fail(http.expectOne(`${url}/17/status`), 'concurrent_modification', 409);
    http.expectOne(`${url}/games/17`).error(new ProgressEvent('error'));
    fixture.detectChanges();
    click('retry-library');
    missing();
    expect(element('[role="alert"]')).toBeNull();
    expect(button('save-library').disabled).toBe(true);
  });

  it('shows a generic network failure on a mutation without a success event', () => {
    existing();
    click('save-library');
    http.expectOne(`${url}/17/status`).error(new ProgressEvent('error'));
    fixture.detectChanges();
    expect(element('[role="alert"]')?.textContent).toContain('Revisá tu conexión');
    expect(element('[role="status"]')?.textContent?.trim()).toBe('');
    expect(changed).not.toHaveBeenCalled();
  });

  it('leaves 401 handling to the interceptor', () => {
    http.expectOne(`${url}/games/17`).flush(null, { status: 401, statusText: 'Unauthorized' });
    fixture.detectChanges();
    expect(element('[role="alert"]')).toBeNull();
  });

  it('reuses the rate-limit countdown and requires explicit retry after expiry', () => {
    vi.useFakeTimers();
    existing();
    const operation = new Subject<LibraryEntryDto>();
    vi.spyOn(service, 'changeStatus').mockReturnValueOnce(operation);
    click('save-library');
    operation.error(
      new RateLimitError(
        new HttpErrorResponse({ status: 429, headers: new HttpHeaders({ 'Retry-After': '2' }) }),
      ),
    );
    fixture.detectChanges();
    expect(element('[role="alert"]')?.textContent).toContain('2 segundos');
    expect(button('save-library').disabled).toBe(true);
    expect(button('choose-backlog').disabled).toBe(true);
    vi.advanceTimersByTime(1000);
    fixture.detectChanges();
    expect(element('[role="alert"]')?.textContent).toContain('1 segundo');
    vi.advanceTimersByTime(1000);
    fixture.detectChanges();
    expect(button('save-library').disabled).toBe(false);
    http.expectNone(`${url}/17/status`);
    click('save-library');
    http.expectOne(`${url}/17/status`).flush(ENTRY);
    fixture.detectChanges();
    expect(element('[role="alert"]')).toBeNull();
  });

  it('supports arrows, wraparound, Home and End with roving focus and radio state', () => {
    missing();
    expect(element('[role="radiogroup"]')?.getAttribute('aria-labelledby')).toBe(
      'library-state-label',
    );
    expect(button('choose-playing').tabIndex).toBe(0);
    const steps = [
      ['playing', 'ArrowLeft', 'completed'],
      ['completed', 'ArrowRight', 'playing'],
      ['playing', 'ArrowDown', 'backlog'],
      ['backlog', 'ArrowUp', 'playing'],
      ['playing', 'End', 'completed'],
      ['completed', 'Home', 'playing'],
    ];
    for (const [from, key, to] of steps) {
      button(`choose-${from}`).dispatchEvent(
        new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }),
      );
      fixture.detectChanges();
      expect(document.activeElement).toBe(button(`choose-${to}`));
      expect(button(`choose-${to}`).getAttribute('aria-checked')).toBe('true');
      expect(element('[role="radio"][tabindex="0"]')).toBe(button(`choose-${to}`));
    }
    expect(button('save-library').disabled).toBe(false);
  });

  it('cancels the old lookup and resets state when the local game ID changes', () => {
    const first = http.expectOne(`${url}/games/17`);
    fixture.componentRef.setInput('gameId', 18);
    fixture.componentRef.setInput('gameName', 'Hades');
    fixture.detectChanges();
    expect(first.cancelled).toBe(true);
    http
      .expectOne(`${url}/games/18`)
      .flush({ ...ENTRY, gameId: 18, name: 'Hades', status: 'completed' });
    fixture.detectChanges();
    expect(element('[data-library-status]')?.textContent).toBe('Completado');
    expect(() => first.flush(ENTRY)).toThrow();
  });

  it.each(['add', 'save', 'remove'])(
    'cancels a pending %s on game change without mixing results or emitting success',
    (action) => {
      if (action === 'add') {
        missing();
        click('choose-playing');
      } else existing();
      if (action !== 'remove') click('save-library');
      else {
        click('remove-library');
        click('confirm-remove');
      }
      const first = http.expectOne(
        action === 'add' ? url : `${url}/17${action === 'save' ? '/status' : ''}`,
      );
      fixture.componentRef.setInput('gameId', 18);
      fixture.componentRef.setInput('gameName', 'Hades');
      fixture.detectChanges();
      expect(first.cancelled).toBe(true);
      fail(http.expectOne(`${url}/games/18`), 'not_in_library', 404);
      expect(element('[data-library-status]')).toBeNull();
      expect(element('[role="group"]')).toBeNull();
      expect(element('[role="status"]')?.textContent?.trim()).toBe('');
      expect(changed).not.toHaveBeenCalled();
      expect(button('save-library').disabled).toBe(true);
    },
  );

  it('unsubscribes from an unfinished lookup when the modal section is destroyed', () => {
    const request = http.expectOne(`${url}/games/17`);
    fixture.destroy();
    expect(request.cancelled).toBe(true);
  });
});
