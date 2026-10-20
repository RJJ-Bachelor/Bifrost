import { Component, input } from '@angular/core';

@Component({
  selector: 'bifrost-list-row',
  imports: [],
  templateUrl: './list-row.html',
  styleUrl: './list-row.scss',
})
export class ListRow {
  readonly muted = input<boolean>(false);
}
