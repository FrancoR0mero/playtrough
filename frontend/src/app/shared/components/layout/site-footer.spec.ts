import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { SiteFooterComponent } from './site-footer';

describe('Site footer', () => {
  it('credits RAWG with a safe active link and preserves the existing footer', () => {
    TestBed.configureTestingModule({
      imports: [SiteFooterComponent],
      providers: [provideRouter([])],
    });
    const fixture = TestBed.createComponent(SiteFooterComponent);
    fixture.detectChanges();
    const footer = fixture.nativeElement.querySelector('footer') as HTMLElement;
    expect(footer.textContent).toContain('Cada juego, una historia.');
    expect(footer.textContent).toContain('Datos e imágenes de videojuegos provistos por');
    const link = footer.querySelector('a[href="https://rawg.io"]')!;
    expect(link.textContent?.trim()).toBe('RAWG');
    expect(link.getAttribute('target')).toBe('_blank');
    expect(link.getAttribute('rel')).toBe('noopener noreferrer');
  });
});
