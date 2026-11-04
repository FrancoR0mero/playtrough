import { Dialog } from '@angular/cdk/dialog';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { throwError } from 'rxjs';
import { environment } from '../../../environments/environment';
import { LibraryService } from '../library/services/library';
import { GameDetailComponent } from './game-detail';

describe('Review editor in a real CDK dialog', () => {
  it('uses one dialog, traps Escape in the editor and closes from the detail with focus restoration', async () => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: LibraryService,
          useValue: {
            getEntry: () =>
              throwError(
                () => new HttpErrorResponse({ status: 404, error: { code: 'not_in_library' } }),
              ),
          },
        },
      ],
    });
    const http = TestBed.inject(HttpTestingController);
    const origin = document.createElement('button');
    document.body.append(origin);
    origin.focus();
    const dialogs = TestBed.inject(Dialog);
    const game = {
      id: 17,
      externalId: 4200,
      name: 'Portal 2',
      slug: 'portal-2',
      coverUrl: null,
      coverUrlLarge: null,
      platforms: [],
      genres: [],
      developers: [],
      summary: '',
      ratingSummary: { average: null, count: 0 },
    };
    const ref = dialogs.open(GameDetailComponent, {
      data: game,
      ariaLabelledBy: 'game-detail-title',
      autoFocus: '[data-action="close"]',
      restoreFocus: origin,
    });
    const detect = async () => {
      TestBed.tick();
      await Promise.resolve();
      TestBed.tick();
    };
    const button = (action: string) =>
      document.querySelector<HTMLButtonElement>(`[data-action="${action}"]`)!;
    const escape = () =>
      document.activeElement!.dispatchEvent(
        new KeyboardEvent('keydown', {
          key: 'Escape',
          keyCode: 27,
          bubbles: true,
          cancelable: true,
        }),
      );
    try {
      await detect();
      http.expectOne(`${environment.apiUrl}/games/4200`).flush(game);
      await detect();
      http
        .expectOne(`${environment.apiUrl}/games/17/reviews/me`)
        .flush({ code: 'review_not_found' }, { status: 404, statusText: 'Not Found' });
      await detect();
      button('write-review').click();
      await detect();
      expect(document.querySelectorAll('[role="dialog"]')).toHaveLength(1);
      expect(document.activeElement?.id).toBe('game-detail-title');
      escape();
      await detect();
      expect(dialogs.openDialogs).toHaveLength(1);
      expect(document.querySelector('textarea')).toBeNull();
      expect(document.activeElement).toBe(button('write-review'));
      button('write-review').click();
      await detect();
      const area = document.querySelector('textarea')!;
      area.value = 'Borrador';
      area.dispatchEvent(new Event('input'));
      await detect();
      area.focus();
      escape();
      await detect();
      expect(dialogs.openDialogs).toHaveLength(1);
      expect(button('discard-review')).not.toBeNull();
      expect(document.activeElement).toBe(button('keep-review'));
      button('discard-review').click();
      await detect();
      expect(document.activeElement).toBe(button('write-review'));
      escape();
      await detect();
      expect(dialogs.openDialogs).toHaveLength(0);
      expect(document.activeElement).toBe(origin);
      http.verify();
    } finally {
      ref.close();
      origin.remove();
    }
  });
});
