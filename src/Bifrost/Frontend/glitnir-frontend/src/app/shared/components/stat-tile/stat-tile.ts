import { Component, input } from '@angular/core';

export interface StatTileItem {
  value: string | number;
  label: string;
}

@Component({
  selector: 'bifrost-stat-tile',
  styleUrl: './stat-tile.scss',
  templateUrl: './stat-tile.html',
})
export class StatTile {
  readonly items = input.required<StatTileItem[]>();
}
