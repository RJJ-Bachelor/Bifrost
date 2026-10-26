import { Component, computed, inject, input, linkedSignal, output, signal } from '@angular/core';
import { FormField, form, validate } from '@angular/forms/signals';
import { Dialog } from '../../../../shared/components/dialog/dialog';
import { FaqItem } from '../../faq-item.model';
import { FaqApi } from '../../services/faq-api';

interface FaqFormModel {
  question: string;
  answer: string;
}

export type FaqDialogState =
  | { mode: 'closed' }
  | { mode: 'create' }
  | { mode: 'edit'; item: FaqItem };

export interface FaqSavedEvent {
  question: string;
  answer: string;
  mode: 'create' | 'edit';
}

@Component({
  selector: 'bifrost-faq-form-dialog',
  imports: [Dialog, FormField],
  templateUrl: './faq-form-dialog.html',
  styleUrl: './faq-form-dialog.scss',
})
export class FaqFormDialog {
  private readonly faqApi = inject(FaqApi);

  readonly state = input.required<FaqDialogState>();
  readonly sessionId = input.required<string>();

  readonly saved = output<FaqSavedEvent>();
  readonly cancelled = output<void>();

  protected readonly dialogOpen = computed(() => this.state().mode !== 'closed');
  protected readonly isEditing = computed(() => this.state().mode === 'edit');
  protected readonly title = computed(() =>
    this.isEditing() ? 'Rediger FAQ' : 'Tilføj FAQ',
  );

  // linkedSignal: the form model resets automatically when the dialog state changes.
  // Documented use case for linkedSignal (mutable derived state).
  protected readonly model = linkedSignal<FaqDialogState, FaqFormModel>({
    source: () => this.state(),
    computation: (state) => ({
      question: state.mode === 'edit' ? state.item.question : '',
      answer: state.mode === 'edit' ? state.item.answer : '',
    }),
  });

  protected readonly faqForm = form(this.model, (path) => {
    validate(path.question, ({ value }) =>
      value().trim().length === 0
        ? { kind: 'required', message: 'Skriv et spørgsmål' }
        : null,
    );
    validate(path.answer, ({ value }) =>
      value().trim().length === 0
        ? { kind: 'required', message: 'Skriv et svar' }
        : null,
    );
  });

  protected readonly submitting = signal(false);
  protected readonly error = signal<string | null>(null);

  protected onCancel(): void {
    this.error.set(null);
    this.cancelled.emit();
  }

  protected onSubmit(event: Event): void {
    event.preventDefault();
    if (this.faqForm().invalid() || this.submitting()) return;

    const state = this.state();
    if (state.mode === 'closed') return;

    const mode = state.mode;
    const { question, answer } = this.model();

    this.submitting.set(true);
    this.error.set(null);

    const request$ =
      state.mode === 'edit'
        ? this.faqApi.updateFaq(state.item.id, question, answer)
        : this.faqApi.createFaq(this.sessionId(), question, answer);

    request$.subscribe({
      next: () => {
        this.submitting.set(false);
        this.saved.emit({ question, answer, mode });
      },
      error: () => {
        this.submitting.set(false);
        this.error.set('Kunne ikke gemme FAQ. Prøv igen.');
      },
    });
  }
}
