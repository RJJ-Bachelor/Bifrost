import { Component, computed, input, linkedSignal, output, signal, untracked } from '@angular/core';
import { Dialog } from '../../../../../../shared/components/dialog/dialog';
import { Session } from '../../../../../session/session.model';

@Component({
  selector: 'bifrost-session-settings-dialog',
  imports: [Dialog],
  templateUrl: './session-settings-dialog.html',
  styleUrl: './session-settings-dialog.scss',
})
export class SessionSettingsDialog {
  readonly open = input.required<boolean>();
  readonly session = input<Session | undefined>(undefined);

  readonly closed = output<void>();
  readonly deleteRequested = output<void>();

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
    if (title.length === 0 || this.renaming()) return;
    // Intentional no-op until BF-47 (backend rename endpoint) is available.
    // The UI mirrors the "saving" state so the flow can be exercised end-to-end,
    // but no persistence happens. This will not ship to users before BF-47 lands.
    this.renaming.set(true);
    this.renameError.set(null);
    setTimeout(() => {
      this.renaming.set(false);
    }, 200);
  }

  protected onRequestDelete(): void {
    this.deleteRequested.emit();
  }
}
