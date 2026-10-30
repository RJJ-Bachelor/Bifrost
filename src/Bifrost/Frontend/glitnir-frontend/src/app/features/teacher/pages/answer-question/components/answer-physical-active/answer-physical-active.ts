import { Component, input, output } from '@angular/core';

@Component({
  selector: 'bifrost-answer-physical-active',
  styleUrl: './answer-physical-active.scss',
  templateUrl: './answer-physical-active.html',
})
export class AnswerPhysicalActive {
  readonly displayNumber = input.required<string>();
  readonly question = input.required<string>();
  readonly busy = input.required<boolean>();
  readonly error = input<string | null>(null);

  readonly complete = output<void>();
}
