import { Component, computed, input, output } from '@angular/core';
import { ListRow } from '../../../../shared/components/list-row/list-row';
import { HelpRequest, HelpRequestStatus } from '../../help-request.model';

const STATUS_LABELS: Record<HelpRequestStatus, string> = {
  pending: 'Venter i kø',
  active: 'Underviser på vej',
  awaitingConfirmation: 'Afventer bekræftelse',
  completed: 'Besvaret',
  cancelled: 'Annulleret',
};

const OPEN_STATUSES: HelpRequestStatus[] = [
  'pending',
  'active',
  'awaitingConfirmation',
];

@Component({
  selector: 'bifrost-question-list',
  imports: [ListRow],
  templateUrl: './question-list.html',
  styleUrl: './question-list.scss',
})
export class QuestionList {
  readonly items = input.required<HelpRequest[]>();

  protected readonly sorted = computed(() =>
    [...this.items()].sort((a, b) => b.requestNumber - a.requestNumber),
  );

  readonly questionSelected = output<HelpRequest>();

  protected label(status: HelpRequestStatus): string {
    return STATUS_LABELS[status];
  }

  protected isOpen(status: HelpRequestStatus): boolean {
    return OPEN_STATUSES.includes(status);
  }
}
