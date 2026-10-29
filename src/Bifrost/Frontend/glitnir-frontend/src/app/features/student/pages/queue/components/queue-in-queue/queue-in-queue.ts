import { Component, input, output } from '@angular/core';

@Component({
  selector: 'bifrost-queue-in-queue',
  templateUrl: './queue-in-queue.html',
  styleUrl: './queue-in-queue.scss',
})
export class QueueInQueue {
  readonly displayNumber = input.required<string>();
  readonly position = input.required<number | null>();
  readonly waitLabel = input.required<string | null>();
  readonly estimatedWait = input.required<number | null>();
  readonly question = input.required<string>();
  readonly awaitingFaq = input.required<boolean>();

  readonly cancelled = output<void>();
}
