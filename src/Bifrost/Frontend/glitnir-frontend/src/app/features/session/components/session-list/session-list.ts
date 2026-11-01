import { Component, computed, input, output } from '@angular/core';
import { Session } from '../../session.model';
import { ListRow } from '../../../../shared/components/list-row/list-row';

@Component({
  selector: 'bifrost-session-list',
  imports: [ListRow],
  templateUrl: './session-list.html',
  styleUrl: './session-list.scss',
})
export class SessionList {
  readonly sessions = input.required<Session[]>();
  readonly sessionSelected = output<Session>();

  protected readonly showWaiting = computed(() =>
    this.sessions().some((session) => session.waitingCount !== undefined),
  );
}
