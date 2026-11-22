import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { SocialGroup } from './groups-api';

@Component({
  selector: 'app-group-classification-summary',
  templateUrl: './group-classification-summary.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GroupClassificationSummaryComponent {
  readonly group = input.required<SocialGroup>();
  readonly compact = input(false);

  protected readonly subgenres = computed(() =>
    this.compact() ? this.group().subgenres.slice(0, 3) : this.group().subgenres,
  );
  protected readonly tags = computed(() =>
    this.compact() ? this.group().tags.slice(0, 3) : this.group().tags,
  );
}
