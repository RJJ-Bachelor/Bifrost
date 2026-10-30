import { Component, computed, inject, input, signal } from '@angular/core';
import { Router } from '@angular/router';
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
    if (this.busy()) return;
    this.busy.set(true);
    this.actionError.set(null);
    this.helpRequestApi.answerTeacherRequest(this.requestId(), text).subscribe({
      next: () => {
        this.busy.set(false);
        this.request.reload();
      },
      error: () => {
        this.busy.set(false);
        this.actionError.set('Kunne ikke sende svaret. Prøv igen.');
      },
    });
  }

  protected submitFaqAnswer(submission: FaqAnswerSubmission): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.actionError.set(null);
    this.helpRequestApi
      .answerTeacherRequest(this.requestId(), submission.text, submission.question)
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.request.reload();
        },
        error: () => {
          this.busy.set(false);
          this.actionError.set('Kunne ikke sende FAQ-svaret. Prøv igen.');
        },
      });
  }

  protected start(): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.actionError.set(null);
    this.helpRequestApi.changeTeacherStatus(this.requestId(), 'start').subscribe({
      next: () => {
        this.busy.set(false);
        this.request.reload();
      },
      error: () => {
        this.busy.set(false);
        this.actionError.set('Kunne ikke starte hjælpen. Prøv igen.');
      },
    });
  }

  protected complete(): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.actionError.set(null);
    this.helpRequestApi.changeTeacherStatus(this.requestId(), 'complete').subscribe({
      next: () => {
        this.busy.set(false);
        this.backToSession();
      },
      error: () => {
        this.busy.set(false);
        this.actionError.set('Kunne ikke afslutte hjælpen. Prøv igen.');
      },
    });
  }

  protected cancel(): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.actionError.set(null);
    this.helpRequestApi.changeTeacherStatus(this.requestId(), 'cancel').subscribe({
      next: () => {
        this.busy.set(false);
        this.backToSession();
      },
      error: () => {
        this.busy.set(false);
        this.actionError.set('Spørgsmålet kunne ikke lægges tilbage i køen. Prøv igen.');
      },
    });
  }

  protected backToSession(): void {
    this.router.navigate(['/teacher/sessions', this.id()]);
  }
}
