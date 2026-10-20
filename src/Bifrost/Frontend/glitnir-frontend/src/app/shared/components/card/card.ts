import { Component, input } from '@angular/core';

@Component({
  selector: 'bifrost-card',
  imports: [],
  templateUrl: './card.html',
  styleUrl: './card.scss',
})
export class Card {
  readonly title = input<string>();
}
