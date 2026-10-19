import { Component, input } from '@angular/core';

@Component({
  selector: 'bifrost-card',
  imports: [],
  templateUrl: './card.html',
  styleUrl: './card.scss',
  host: {
    "[class.card--teacher]": "variant() === 'teacher'",
  },
})
export class Card {
  readonly title = input<string>();
  readonly variant = input<'student' | 'teacher'>('student');
}
