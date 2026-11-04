import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { MyReviewComponent } from './my-review';
import { ReviewService } from './services/review';

const REVIEW = {
  id: 1,
  gameId: 17,
  rating: 4,
  text: 'Anterior',
  createdAt: '2026-11-01T12:00:00Z',
  editedAt: null,
};

describe('Review request lifetime', () => {
  let fixture: ComponentFixture<MyReviewComponent>;
  let http: HttpTestingController;
  const url = (gameId: number) => `${environment.apiUrl}/games/${gameId}/reviews/me`;
  function click(action: string) {
    fixture.nativeElement.querySelector(`[data-action="${action}"]`).click();
    fixture.detectChanges();
  }
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [MyReviewComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(MyReviewComponent);
    fixture.componentRef.setInput('gameId', 17);
    fixture.detectChanges();
  });
  afterEach(() => {
    try {
      http.verify();
    } finally {
      fixture.destroy();
    }
  });

  it.each(['load', 'save', 'delete'])(
    'cancels pending %s and clears old data when the game changes',
    (operation) => {
      if (operation !== 'load') {
        http.expectOne(url(17)).flush(REVIEW);
        fixture.detectChanges();
        if (operation === 'save') {
          click('edit-review');
          const area = fixture.nativeElement.querySelector('textarea');
          area.value = 'No mostrar en otro juego';
          area.dispatchEvent(new Event('input'));
          fixture.detectChanges();
          click('save-review');
        } else {
          click('remove-review');
          click('confirm-remove-review');
        }
      }
      const pending = http.expectOne(url(17));
      fixture.componentRef.setInput('gameId', 18);
      fixture.detectChanges();
      expect(pending.cancelled).toBe(true);
      expect(fixture.nativeElement.querySelector('textarea')).toBeNull();
      http.expectOne(url(18)).flush({ ...REVIEW, gameId: 18, text: 'Nuevo juego' });
      fixture.detectChanges();
      expect(fixture.nativeElement.textContent).toContain('Nuevo juego');
      expect(fixture.nativeElement.textContent).not.toContain('Anterior');
      expect(fixture.nativeElement.textContent).not.toContain('No mostrar en otro juego');
      TestBed.inject(ReviewService).save(17, 5, 'Respuesta del anterior').subscribe();
      http.expectOne(url(17)).flush({ review: REVIEW, ratingSummary: { average: 4, count: 1 } });
      fixture.detectChanges();
      expect(fixture.nativeElement.textContent).toContain('Nuevo juego');
      expect(fixture.nativeElement.textContent).not.toContain('Anterior');
    },
  );

  it('cancels the lookup on destruction', () => {
    const pending = http.expectOne(url(17));
    fixture.destroy();
    expect(pending.cancelled).toBe(true);
  });
});
