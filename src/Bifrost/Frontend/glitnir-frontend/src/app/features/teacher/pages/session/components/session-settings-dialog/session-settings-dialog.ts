import { Component, computed, inject, input, linkedSignal, output, signal, untracked } from '@angular/core';
import { Dialog } from '../../../../../../shared/components/dialog/dialog';
import { SessionApi, type Session } from '../../../../../session';

@Component({
  selector: 'bifrost-session-settings-dialog',
  imports: [Dialog],
  templateUrl: './session-settings-dialog.html',
  styleUrl: './session-settings-dialog.scss',
})
export class SessionSettingsDialog {
  private readonly sessionApi = inject(SessionApi);

  readonly open = input.required<boolean>();
  readonly session = input<Session | undefined>(undefined);

  readonly closed = output<void>();
  readonly deleteRequested = output<void>();
  readonly resetRequested = output<void>();
  readonly renamed = output<Session>();

  protected readonly studentCode = computed(() => this.session()?.code ?? '—');
  protected readonly teacherCode = computed(() => this.session()?.teacherCode ?? '—');

  // linkedSignal: reset rename model to session title on open, preserve user input while open.
  protected readonly renameModel = linkedSignal<boolean, string>({
    source: () => this.open(),
    computation: (isOpen) =>
      isOpen ? (untracked(() => this.session()?.title) ?? '') : '',
  });

  protected readonly renaming = signal(false);
  protected readonly renameError = linkedSignal<boolean, string | null>({
    source: () => this.open(),
    computation: () => null,
  });

  protected onClose(): void {
    this.closed.emit();
  }

  protected onRenameInput(event: Event): void {
    this.renameModel.set((event.target as HTMLInputElement).value);
  }

  protected onSubmitRename(event: Event): void {
    event.preventDefault();
    const title = this.renameModel().trim();
    const session = this.session();
    if (title.length === 0 || session === undefined || this.renaming()) return;

    this.renaming.set(true);
    this.renameError.set(null);

    this.sessionApi.renameSession(session.id, title).subscribe({
      next: (updated) => {
        this.renaming.set(false);
        this.renamed.emit(updated);
      },
      error: () => {
        this.renaming.set(false);
        this.renameError.set('Kunne ikke omdøbe sessionen. Prøv igen.');
      },
    });
  }

  protected onRequestDelete(): void {
    this.deleteRequested.emit();
  }

  protected onRequestReset(): void {
    this.resetRequested.emit();
  }
}
