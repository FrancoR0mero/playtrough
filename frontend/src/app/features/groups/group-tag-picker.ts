import { ChangeDetectionStrategy, Component, computed, input, model } from '@angular/core';
import { GroupTag, GroupTagCategory } from './groups-api';

const CATEGORIES: ReadonlyArray<{ id: GroupTagCategory; name: string }> = [
  { id: 'modalidad', name: 'Modalidad' },
  { id: 'estilo', name: 'Estilo' },
  { id: 'plataforma', name: 'Plataformas' },
];

@Component({
  selector: 'app-group-tag-picker',
  templateUrl: './group-tag-picker.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GroupTagPickerComponent {
  readonly tags = input.required<GroupTag[]>();
  readonly selected = model.required<string[]>();
  protected readonly sections = computed(() =>
    CATEGORIES.map((category) => ({
      ...category,
      tags: this.tags().filter((tag) => tag.category === category.id),
    })).filter((category) => category.tags.length > 0),
  );

  protected toggle(id: string, checked: boolean): void {
    this.selected.update((selected) =>
      checked ? [...selected, id] : selected.filter((item) => item !== id),
    );
  }
}
