import { Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { RealtimeService } from '../../../../core/realtime/realtime.service';
import { Topbar } from '../../../../core/layout/topbar/topbar';
import { Card } from '../../../../shared/components/card/card';
import { Dialog } from '../../../../shared/components/dialog/dialog';
import { FaqApi, FaqArchiveList, FaqList } from '../../../faq';
import {
  HelpRequestApi,
  HelpTypeToggle,
  MessageForm,
  QuestionList,
  type HelpRequest,
  type HelpType,
} from '../../../help-request';
import { SessionApi, type SessionGeneration } from '../../../session';

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
  private readonly realtime = inject(RealtimeService);
  private readonly destroyRef = inject(DestroyRef);

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

  constructor() {
    this.realtime.notifications$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((notification) => {
        if (notification.sessionId !== this.id()) return;
        if (notification.eventType === 'SessionChanged' && notification.change === 'FaqAnswered') {
          this.faq.reload();
        }
      });
  }

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
