import { Component, inject, input, linkedSignal, output, signal } from '@angular/core';
import { Dialog } from '../../../../../../shared/components/dialog/dialog';
import { SessionApi } from '../../../../../session';

@Component({
  selector: 'bifrost-session-reset-dialog',
  imports: [Dialog],
  templateUrl: './session-reset-dialog.html',
  styleUrl: './session-reset-dialog.scss',
})
export class SessionResetDialog {
  private readonly sessionApi = inject(SessionApi);

  readonly open = input.required<boolean>();
  readonly sessionId = input.required<string>();
  readonly generation = input<number | undefined>(undefined);

  readonly cleared = output<void>();
  readonly cancelled = output<void>();

  protected readonly clearing = signal(false);
  protected readonly error = linkedSignal<boolean, string | null>({
    source: () => this.open(),
    computation: () => null,
  });

  protected onCancel(): void {
    this.cancelled.emit();
  }

  protected onConfirm(): void {
    const generation = this.generation();
    if (generation === undefined || this.clearing()) return;

    this.clearing.set(true);
    this.error.set(null);

    this.sessionApi.clearSession(this.sessionId(), generation).subscribe({
      next: () => {
        this.clearing.set(false);
        this.cleared.emit();
      },
      error: () => {
        this.clearing.set(false);
        this.error.set('Kunne ikke rydde sessionsdata. Prøv igen.');
      },
    });
  }
}
