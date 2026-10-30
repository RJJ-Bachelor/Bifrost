import { Component, input, output } from '@angular/core';
import { FaqCard } from '../../../../../faq';

@Component({
  imports: [FaqCard],
  selector: 'bifrost-queue-awaiting-confirmation',
  templateUrl: './queue-awaiting-confirmation.html',
  styleUrl: './queue-awaiting-confirmation.scss',
})
export class QueueAwaitingConfirmation {
  readonly question = input.required<string>();
  readonly faqQuestion = input<string | null>(null);
  readonly faqAnswer = input<string | null>(null);
  readonly responding = input.required<boolean>();
  readonly error = input<string | null>(null);

  readonly responded = output<boolean>();
}
