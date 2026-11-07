/** Match ReviewService.Sanitize: normalize newlines, remove controls, trim .NET whitespace. */
export function normalizeReviewText(text: string): string {
  return (
    text
      .replace(/\r\n?/g, '\n')
      // eslint-disable-next-line no-control-regex
      .replace(/[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f-\u009f]/g, '')
      .replace(/^\p{White_Space}+|\p{White_Space}+$/gu, '')
  );
}

export function reviewTextLength(text: string): number {
  return Array.from(normalizeReviewText(text)).length;
}
