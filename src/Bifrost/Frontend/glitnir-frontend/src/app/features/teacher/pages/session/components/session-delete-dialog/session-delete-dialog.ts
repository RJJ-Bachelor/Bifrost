import { Component, inject, input, linkedSignal, output, signal } from '@angular/core';
import { Dialog } from '../../../../../../shared/components/dialog/dialog';
import { SessionApi } from '../../../../../session/services/session-api';

@Component({
  selector: 'bifrost-session-delete-dialog',
  imports: [Dialog],
  templateUrl: './session-delete-dialog.html',
  styleUrl: './session-delete-dialog.scss',
})
export class SessionDeleteDialog {
  private readonly sessionApi = inject(SessionApi);

  readonly open = input.required<boolean>();
  readonly sessionId = input.required<string>();

  readonly deleted = output<void>();
  readonly cancelled = output<void>();

  protected readonly deleting = signal(false);
  protected readonly error = linkedSignal<boolean, string | null>({
    source: () => this.open(),
    computation: () => null,
  });

  protected onCancel(): void {
    this.cancelled.emit();
  }

  protected onConfirm(): void {
    if (this.deleting()) return;

    this.deleting.set(true);
    this.sessionApi.deleteSession(this.sessionId()).subscribe({
      next: () => {
        this.deleting.set(false);
        this.deleted.emit();
      },
      error: () => {
        this.deleting.set(false);
        this.error.set('Kunne ikke slette sessionen. Prøv igen.');
      },
    });
  }
}
