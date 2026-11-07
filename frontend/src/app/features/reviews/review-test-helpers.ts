import { HttpErrorResponse } from '@angular/common/http';
import { EMPTY, throwError } from 'rxjs';
import { ReviewService } from './services/review';

/** Keep unrelated catalog/library suites independent of review HTTP requests. */
export const NO_REVIEW_PROVIDER = {
  provide: ReviewService,
  useValue: {
    changes$: EMPTY,
    getMine: () =>
      throwError(
        () =>
          new HttpErrorResponse({
            status: 404,
            error: { extensions: { code: 'review_not_found' } },
          }),
      ),
  },
};
