import { normalizeReviewText, reviewTextLength } from './review-text';

describe('Review text backend parity', () => {
  it('normalizes CRLF and CR, removes controls and trims Unicode whitespace', () => {
    expect(normalizeReviewText('\u0085\u00a0 A\r\nB\rC\u0000\tD \u3000')).toBe('A\nB\nC\tD');
  });
  it('counts Unicode scalars, including combining marks, rather than UTF-16 units', () => {
    expect(reviewTextLength('😀'.repeat(2000))).toBe(2000);
    expect(reviewTextLength('e\u0301')).toBe(2);
    expect(reviewTextLength('  \r\n\t  ')).toBe(0);
    // .NET Trim does not remove the zero-width no-break space.
    expect(reviewTextLength('\ufeff')).toBe(1);
  });
});
