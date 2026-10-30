import {
  Component,
  ElementRef,
  input,
  linkedSignal,
  output,
  signal,
  viewChild,
} from '@angular/core';

export interface FaqAnswerSubmission {
  text: string;
  question?: string;
}

@Component({
  selector: 'bifrost-faq-card',
  styleUrl: './faq-card.scss',
  templateUrl: './faq-card.html',
})
export class FaqCard {
  readonly question = input.required<string>();
  readonly answer = input<string>('');
  readonly editable = input(false);
  readonly busy = input(false);
  readonly error = input<string | null>(null);

  readonly answered = output<FaqAnswerSubmission>();

  private readonly questionInput = viewChild<ElementRef<HTMLInputElement>>('questionInput');

  protected readonly editingQuestion = signal(false);
  protected readonly editedQuestion = linkedSignal(() => this.question());
  protected readonly answerDraft = signal('');

  protected startEditingQuestion(): void {
    this.editingQuestion.set(true);
    setTimeout(() => {
      const input = this.questionInput()?.nativeElement;
      if (input) {
        input.focus();
        input.select();
      }
    });
  }

  protected saveQuestion(): void {
    if (this.editedQuestion().trim().length === 0) {
      this.editedQuestion.set(this.question());
    }
    this.editingQuestion.set(false);
  }

  protected cancelEditing(): void {
    this.editedQuestion.set(this.question());
    this.editingQuestion.set(false);
  }

  protected onQuestionInput(event: Event): void {
    this.editedQuestion.set((event.target as HTMLInputElement).value);
  }

  protected onQuestionKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter') {
      event.preventDefault();
      (event.target as HTMLInputElement).blur();
    } else if (event.key === 'Escape') {
      event.preventDefault();
      this.cancelEditing();
    }
  }

  protected onAnswerInput(event: Event): void {
    this.answerDraft.set((event.target as HTMLTextAreaElement).value);
  }

  protected submitAnswer(): void {
    if (this.busy()) return;
    const text = this.answerDraft().trim();
    if (text.length === 0) return;

    const edited = this.editedQuestion().trim();
    const questionChanged = edited.length > 0 && edited !== this.question();

    this.answered.emit({
      text,
      question: questionChanged ? edited : undefined,
    });
  }
}
