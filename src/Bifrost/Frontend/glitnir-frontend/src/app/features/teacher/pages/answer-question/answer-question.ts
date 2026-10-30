import { Component, computed, inject, input, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Observable } from 'rxjs';
import { Topbar } from '../../../../core/layout/topbar/topbar';
import { Card } from '../../../../shared/components/card/card';
import { HelpRequestApi } from '../../../help-request';
import { SessionApi } from '../../../session';
import { type FaqAnswerSubmission, FaqCard } from '../../../faq';
import { AnswerDigitalChat } from './components/answer-digital-chat/answer-digital-chat';
import { AnswerPhysicalActive } from './components/answer-physical-active/answer-physical-active';
import { AnswerPhysicalPrepare } from './components/answer-physical-prepare/answer-physical-prepare';

type AnswerViewState =
  | 'loading'
  | 'digital-chat'
  | 'faq-answer'
  | 'physical-prepare'
  | 'physical-active'
  | 'closed'
  | 'unsupported';

@Component({
  imports: [Topbar, Card, AnswerDigitalChat, FaqCard, AnswerPhysicalPrepare, AnswerPhysicalActive],
  selector: 'bifrost-answer-question',
  styleUrl: './answer-question.scss',
  templateUrl: './answer-question.html',
})
export class AnswerQuestion {
  private readonly router = inject(Router);
  private readonly helpRequestApi = inject(HelpRequestApi);
  private readonly sessionApi = inject(SessionApi);

  readonly id = input.required<string>();
  readonly requestId = input.required<string>();

  protected readonly request = this.helpRequestApi.getTeacherRequest(() => this.requestId());
  protected readonly session = this.sessionApi.getTeacherSession(() => this.id());

  protected readonly viewState = computed<AnswerViewState>(() => {
    const detail = this.request.value();
    if (detail === undefined) return 'loading';
    if (
      detail.status === 'completedByDigital' ||
      detail.status === 'completedByPhysical' ||
      detail.status === 'completedByFaq' ||
      detail.status === 'cancelled'
    ) {
      return 'closed';
    }
    if (detail.status === 'awaitingFaq' || detail.status === 'awaitingConfirmation') {
      return 'unsupported';
    }
    if (detail.isFaq) return 'faq-answer';
    if (detail.helpType === 'digital') return 'digital-chat';
    if (detail.status === 'active') return 'physical-active';
    return 'physical-prepare';
  });

  protected readonly sessionTitle = computed(() => this.session.value()?.title ?? '');

  protected readonly cardTitle = computed(() => {
    switch (this.viewState()) {
      case 'digital-chat':
        return 'Digitalt svar';
      case 'faq-answer':
        return 'FAQ-svar';
      case 'physical-prepare':
        return 'Spørgsmål';
      case 'physical-active':
        return 'Hjælp i gang';
      default:
        return '';
    }
  });

  protected readonly busy = signal(false);
  protected readonly actionError = signal<string | null>(null);

  protected sendAnswer(text: string): void {
    this.runAction(
      this.helpRequestApi.answerTeacherRequest(this.requestId(), text),
      'Kunne ikke sende svaret. Prøv igen.',
      () => this.request.reload(),
    );
  }

  protected submitFaqAnswer(submission: FaqAnswerSubmission): void {
    this.runAction(
      this.helpRequestApi.answerTeacherRequest(
        this.requestId(),
        submission.text,
        submission.question,
      ),
      'Kunne ikke sende FAQ-svaret. Prøv igen.',
      () => this.request.reload(),
    );
  }

  protected start(): void {
    this.runAction(
      this.helpRequestApi.changeTeacherStatus(this.requestId(), 'start'),
      'Kunne ikke starte hjælpen. Prøv igen.',
      () => this.request.reload(),
    );
  }

  protected complete(): void {
    this.runAction(
      this.helpRequestApi.changeTeacherStatus(this.requestId(), 'complete'),
      'Kunne ikke afslutte hjælpen. Prøv igen.',
      () => this.backToSession(),
    );
  }

  protected cancel(): void {
    this.runAction(
      this.helpRequestApi.changeTeacherStatus(this.requestId(), 'cancel'),
      'Spørgsmålet kunne ikke lægges tilbage i køen. Prøv igen.',
      () => this.backToSession(),
    );
  }

  private runAction(action$: Observable<unknown>, errorMessage: string, onSuccess: () => void): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.actionError.set(null);
    action$.subscribe({
      next: () => {
        this.busy.set(false);
        onSuccess();
      },
      error: () => {
        this.busy.set(false);
        this.actionError.set(errorMessage);
      },
    });
  }

  protected backToSession(): void {
    this.router.navigate(['/teacher/sessions', this.id()]);
  }
}
