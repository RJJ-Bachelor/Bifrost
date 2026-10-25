import { Component, input } from '@angular/core';

@Component({
  selector: 'bifrost-stat-tile',
  styleUrl: './stat-tile.scss',
  templateUrl: './stat-tile.html',
})
export class StatTile {
  readonly value = input.required<string | number>();
  readonly label = input.required<string>();
}
