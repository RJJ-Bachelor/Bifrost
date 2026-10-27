import { Component, inject, input, linkedSignal, output, signal } from '@angular/core';
import { Dialog } from '../../../../../../shared/components/dialog/dialog';
import { SessionApi } from '../../../../../session/services/session-api';

@Component({
  selector: 'bifrost-archive-confirm-dialog',
  imports: [Dialog],
  templateUrl: './archive-confirm-dialog.html',
  styleUrl: './archive-confirm-dialog.scss',
})
export class ArchiveConfirmDialog {
  private readonly sessionApi = inject(SessionApi);

  readonly open = input.required<boolean>();
  readonly sessionId = input.required<string>();
  readonly generation = input<number | undefined>(undefined);

  readonly archived = output<void>();
  readonly cancelled = output<void>();

  // Reset title and error automatically when the dialog opens.
  protected readonly title = linkedSignal<boolean, string>({
    source: () => this.open(),
    computation: () => '',
  });

  protected readonly archiving = signal(false);
  protected readonly error = linkedSignal<boolean, string | null>({
    source: () => this.open(),
    computation: () => null,
  });

  protected onTitleInput(event: Event): void {
    this.title.set((event.target as HTMLInputElement).value);
  }

  protected onCancel(): void {
    this.cancelled.emit();
  }

  protected onConfirm(): void {
    const title = this.title().trim();
    const generation = this.generation();
    if (title.length === 0 || generation === undefined || this.archiving()) return;

    this.archiving.set(true);
    this.error.set(null);

    this.sessionApi.archiveSession(this.sessionId(), generation, title).subscribe({
      next: () => {
        this.archiving.set(false);
        this.archived.emit();
      },
      error: () => {
        this.archiving.set(false);
        this.error.set('Kunne ikke arkivere. Prøv igen.');
      },
    });
  }
}
