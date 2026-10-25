import { Component, input } from '@angular/core';

@Component({
  selector: 'bifrost-list-row',
  imports: [],
  templateUrl: './list-row.html',
  styleUrl: './list-row.scss',
})
export class ListRow {
  /**
   * Renders the row in a de-emphasized, non-interactive state.
   * Muted rows do not emit click events to the parent.
   */
  readonly muted = input<boolean>(false);
}
