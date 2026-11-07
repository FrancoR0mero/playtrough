import { Dialog } from '@angular/cdk/dialog';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { throwError } from 'rxjs';
import { environment } from '../../../environments/environment';
import { LibraryService } from '../library/services/library';
import { NO_REVIEW_PROVIDER } from '../reviews/review-test-helpers';
import { GameDetailComponent } from './game-detail';

describe('Game detail reviews navigation', () => {
  it.each([0, 1, 12])(
    'labels count %s, closes then navigates using local ID without restoring card focus',
    async (count) => {
      TestBed.configureTestingModule({
        providers: [
          provideHttpClient(),
          provideHttpClientTesting(),
          provideRouter([]),
          NO_REVIEW_PROVIDER,
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
      const dialogs = TestBed.inject(Dialog);
      const origin = document.createElement('button');
      document.body.append(origin);
      origin.focus();
      const data = {
        id: 17,
        externalId: 4200,
        name: 'Portal 2',
        coverUrl: null,
        coverUrlLarge: null,
        platforms: [],
        genres: [],
        developers: [],
        ratingSummary: { average: count ? 4 : null, count },
      };
      const ref = dialogs.open(GameDetailComponent, {
        data,
        restoreFocus: origin,
        autoFocus: '[data-action="close"]',
      });
      try {
        TestBed.tick();
        http.expectOne(`${environment.apiUrl}/games/4200`).flush(data);
        TestBed.tick();
        await Promise.resolve();
        TestBed.tick();
        const focus = vi.spyOn(origin, 'focus');
        const navigate = vi
          .spyOn(TestBed.inject(Router), 'navigate')
          .mockImplementation(async (commands) => {
            expect(dialogs.openDialogs).toHaveLength(0);
            expect(commands).toEqual(['/games/local', 17, 'reviews']);
            return true;
          });
        const button = document.querySelector<HTMLButtonElement>('[data-action="view-reviews"]')!;
        expect(button.textContent!.trim()).toBe(count ? `Ver reseñas (${count})` : 'Ver reseñas');
        expect(button.tabIndex).toBe(0);
        button.focus();
        button.click();
        TestBed.tick();
        await Promise.resolve();
        expect(navigate).toHaveBeenCalledOnce();
        expect(ref.config.restoreFocus).toBe(false);
        expect(focus).not.toHaveBeenCalled();
        http.verify();
      } finally {
        ref.close();
        origin.remove();
        vi.restoreAllMocks();
      }
    },
  );
});
