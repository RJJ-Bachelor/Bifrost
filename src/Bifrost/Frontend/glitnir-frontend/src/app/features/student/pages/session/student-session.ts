import { Component, computed, inject, input, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Topbar } from '../../../../core/layout/topbar/topbar';
import { Card } from '../../../../shared/components/card/card';
import { Dialog } from '../../../../shared/components/dialog/dialog';
import { FaqArchiveList } from '../../../faq/components/faq-archive-list/faq-archive-list';
import { FaqList } from '../../../faq/components/faq-list/faq-list';
import { FaqApi } from '../../../faq/services/faq-api';
import { HelpTypeToggle } from '../../../help-request/components/help-type-toggle/help-type-toggle';
import { QuestionList } from '../../../help-request/components/question-list/question-list';
import { MessageForm } from '../../../help-request/components/message-form/message-form';
import { HelpRequest, HelpType } from '../../../help-request/help-request.model';
import { HelpRequestApi } from '../../../help-request/services/help-request-api';
import { SessionApi } from '../../../session/services/session-api';
import { SessionGeneration } from '../../../session/session.model';

@Component({
  imports: [Topbar, Card, Dialog, HelpTypeToggle, MessageForm, FaqList, FaqArchiveList, QuestionList],
  selector: 'bifrost-student-session',
  styleUrl: './student-session.scss',
  templateUrl: './student-session.html',
})
export class StudentSession {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly sessionApi = inject(SessionApi);
  private readonly helpRequestApi = inject(HelpRequestApi);
  private readonly faqApi = inject(FaqApi);

  readonly id = input.required<string>();

  protected readonly session = this.sessionApi.getStudentSession(() => this.id());
  protected readonly sessionTitle = computed(() => this.session.value()?.title ?? '');
  protected readonly faq = this.faqApi.getSessionFaq(
    () => this.id(),
    () => this.session.value()?.currentGeneration ?? 0,
  );
  protected readonly status = this.sessionApi.getSessionStatus(() => this.id());
  protected readonly requests = this.helpRequestApi.getStudentRequests(() => this.id());
  protected readonly generations = this.sessionApi.getStudentGenerations(() => this.id());
  protected readonly archiveVisibleCount = computed(() =>
    Math.max(3, this.requests.value().length - 1),
  );
  protected readonly queue = this.helpRequestApi.getStudentQueue(() => this.id());
  protected readonly hasOpenRequest = computed(
    () => this.queue.value().ownRequestId !== null,
  );
  protected readonly helpType = signal<HelpType>('physical');
  protected readonly submitting = signal(false);
  protected readonly submitError = signal<string | null>(null);
  protected readonly selectedRequest = signal<HelpRequest | null>(null);
  protected readonly selectedGeneration = signal<SessionGeneration | null>(null);

  protected switchSession(): void {
    this.router.navigateByUrl('/');
  }

  protected goToQueue(): void {
    this.router.navigate(['queue'], { relativeTo: this.route });
  }

  protected onQuestionSelected(request: HelpRequest): void {
    if (request.status === 'completed' && request.helpType === 'digital') {
      this.selectedRequest.set(request);
      return;
    }
    if (request.status === 'pending' || request.status === 'active' || request.status === 'awaitingConfirmation') {
      this.goToQueue();
    }
  }

  protected closeRequestDialog(): void {
    this.selectedRequest.set(null);
  }

  protected onGenerationSelected(generation: SessionGeneration): void {
    this.selectedGeneration.set(generation);
  }

  protected closeGenerationDialog(): void {
    this.selectedGeneration.set(null);
  }

  protected onQuestionSubmitted(message: string): void {
    if (this.submitting()) return;

    this.submitting.set(true);
    this.submitError.set(null);

    this.helpRequestApi
      .createHelpRequest(this.id(), { message, helpType: this.helpType() })
      .subscribe({
        next: () => {
          this.submitting.set(false);
          this.router.navigate(['queue'], { relativeTo: this.route });
        },
        error: () => {
          this.submitting.set(false);
          this.submitError.set('Kunne ikke sende spørgsmålet. Prøv igen.');
        },
      });
  }
}
