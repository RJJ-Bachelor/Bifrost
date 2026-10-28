import { Component, input } from '@angular/core';

@Component({
  selector: 'bifrost-queue-my-turn',
  templateUrl: './queue-my-turn.html',
  styleUrl: './queue-my-turn.scss',
})
export class QueueMyTurn {
  readonly displayNumber = input.required<string>();
  readonly teacherName = input<string | null>(null);
}
