import { Component, computed, input, output } from '@angular/core';

@Component({
  selector: 'bifrost-answer-physical-prepare',
  styleUrl: './answer-physical-prepare.scss',
  templateUrl: './answer-physical-prepare.html',
})
export class AnswerPhysicalPrepare {
  readonly displayNumber = input.required<string>();
  readonly question = input.required<string>();
  readonly queuedAt = input.required<string>();
  readonly busy = input.required<boolean>();
  readonly error = input<string | null>(null);

  readonly start = output<void>();
  readonly cancelRequested = output<void>();

  protected readonly askedAtLabel = computed(() => {
    const date = new Date(this.queuedAt());
    if (Number.isNaN(date.getTime())) return '';
    const hours = date.getHours().toString().padStart(2, '0');
    const minutes = date.getMinutes().toString().padStart(2, '0');
    return `Stillet kl. ${hours}:${minutes}`;
  });
}
