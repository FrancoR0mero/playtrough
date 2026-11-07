import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

@Component({
  selector: 'app-confirmation-content',
  templateUrl: './confirmation-content.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ConfirmationContentComponent {
  readonly eyebrow = input.required<string>();
  readonly title = input.required<string>();
  readonly titleId = input.required<string>();
  readonly descriptionId = input.required<string>();
  readonly note = input('');
  readonly confirmLabel = input('Confirmar');
  readonly confirmed = output<void>();
  readonly cancelled = output<void>();
}
