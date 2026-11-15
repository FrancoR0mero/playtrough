import { FeedEntryDto } from '../timeline/models/timeline';
import { timelineEntry } from '../timeline/timeline-test-helpers';
export function feedEntry(overrides: Partial<FeedEntryDto> = {}): FeedEntryDto {
  return { ...timelineEntry(), author: { username: 'bea', avatarUrl: null }, ...overrides };
}
