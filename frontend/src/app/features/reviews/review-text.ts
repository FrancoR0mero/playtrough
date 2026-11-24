import { normalizePlainText } from '../../shared/plain-text';

export const normalizeReviewText = normalizePlainText;

export function reviewTextLength(text: string): number {
  return Array.from(normalizeReviewText(text)).length;
}
