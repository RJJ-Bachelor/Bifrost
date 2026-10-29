import { Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { map, of, switchMap, timer } from 'rxjs';
import { Topbar } from '../../../../core/layout/topbar/topbar';
import { Card } from '../../../../shared/components/card/card';
import { Dialog } from '../../../../shared/components/dialog/dialog';
import { RealtimeHub } from '../../../../core/realtime/services/realtime-hub';
import { Chat, HelpRequestApi, type HelpRequest } from '../../../help-request';
import { SessionApi } from '../../../session';
import { QueueAwaitingConfirmation } from './components/queue-awaiting-confirmation/queue-awaiting-confirmation';
import { QueueInQueue } from './components/queue-in-queue/queue-in-queue';
import { QueueMyTurn } from './components/queue-my-turn/queue-my-turn';

type QueueViewState = 'in-queue' | 'my-turn' | 'awaiting-confirmation' | 'completed-digital' | 'none';

@Component({
  imports: [Topbar, Card, Dialog, QueueMyTurn, QueueInQueue, QueueAwaitingConfirmation, Chat],
  selector: 'bifrost-queue',
  styleUrl: './queue.scss',
  templateUrl: './queue.html',
})
export class Queue {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly helpRequestApi = inject(HelpRequestApi);
  private readonly sessionApi = inject(SessionApi);
  private readonly realtime = inject(RealtimeHub);
  private readonly destroyRef = inject(DestroyRef);

  readonly id = input.required<string>();

  protected readonly session = this.sessionApi.getStudentSession(() => this.id());
  protected readonly sessionTitle = computed(() => this.session.value()?.title ?? '');
  protected readonly queue = this.helpRequestApi.getStudentQueue(() => this.id());
  protected readonly requests = this.helpRequestApi.getStudentRequests(() => this.id());

  protected readonly activeRequest = computed<HelpRequest | null>(() => {
    const ownId = this.queue.value().ownRequestId;
    if (ownId === null) return null;
    return this.requests.value().find((item) => item.id === ownId) ?? null;
  });

  protected readonly viewState = computed<QueueViewState>(() => {
    if (this.answeredRequestId() !== null) return 'completed-digital';
    const request = this.activeRequest();
    if (request === null) return 'none';
    switch (request.status) {
      case 'active':
        return 'my-turn';
      case 'awaitingConfirmation':
        return 'awaiting-confirmation';
      case 'completedByDigital':
        return 'completed-digital';
      case 'pending':
      case 'awaitingFaq':
        return 'in-queue';
      default:
        return 'none';
    }
  });

  private readonly detailRequestId = computed(() => {
    if (this.viewState() === 'completed-digital') {
      return this.answeredRequestId();
    }
    if (this.viewState() === 'awaiting-confirmation') {
      return this.activeRequest()?.id ?? null;
    }
    return null;
  });

  protected readonly activeDetail = this.helpRequestApi.getStudentRequest(() => this.detailRequestId());

  protected readonly faqQuestion = computed<string | null>(() => {
    const messages = this.activeDetail.value()?.messages ?? [];
    const faqMessages = messages.filter((m) => m.messageType === 'faqQuestion');
    if (faqMessages.length === 0) return null;
    return faqMessages.reduce((a, b) => (a.sequenceNumber > b.sequenceNumber ? a : b)).text;
  });

  protected readonly faqAnswer = computed<string | null>(() => {
    const messages = this.activeDetail.value()?.messages ?? [];
    const faqMessages = messages.filter((m) => m.messageType === 'faqAnswer');
    if (faqMessages.length === 0) return null;
    return faqMessages.reduce((a, b) => (a.sequenceNumber > b.sequenceNumber ? a : b)).text;
  });

  protected readonly responding = signal(false);
  protected readonly respondError = signal<string | null>(null);

  // Second-tick that runs only while the request is pending in the queue.
  private readonly now = toSignal(
    toObservable(this.activeRequest).pipe(
      switchMap((request) => {
        if (request === null || (request.status !== 'pending' && request.status !== 'awaitingFaq')) {
          return of(Date.now());
        }
        return timer(0, 1_000).pipe(map(() => Date.now()));
      }),
    ),
    { initialValue: Date.now() },
  );

  protected readonly waitLabel = computed(() => {
    const request = this.activeRequest();
    if (request === null) return null;
    const queuedAtMs = new Date(request.queuedAt).getTime();
    if (Number.isNaN(queuedAtMs)) return null;
    const totalSeconds = Math.max(0, Math.floor((this.now() - queuedAtMs) / 1_000));
    const minutes = Math.floor(totalSeconds / 60);
    const seconds = totalSeconds % 60;
    return `${minutes}:${seconds.toString().padStart(2, '0')}`;
  });

  protected readonly cardTitle = computed(() => {
    switch (this.viewState()) {
      case 'my-turn':
        return 'Det er din tur!';
      case 'awaiting-confirmation':
        return 'Generelt svar';
      case 'completed-digital':
        return 'Samtale';
      default:
        return 'Du er i køen';
    }
  });

  protected readonly isAwaitingFaq = computed(() => this.activeRequest()?.status === 'awaitingFaq');

  protected readonly displayNumber = computed(() => this.queue.value().displayNumber ?? '—');
  protected readonly position = computed(() => this.queue.value().position);
  protected readonly estimatedWait = computed(() => this.queue.value().estimatedWaitMinutes);

  protected readonly cancelDialogOpen = signal(false);
  protected readonly cancelling = signal(false);
  protected readonly cancelError = signal<string | null>(null);

  constructor() {
    effect(() => {
      if (this.queue.isLoading() || this.requests.isLoading()) return;
      if (this.viewState() === 'none') {
        this.backToSession();
      }
    });

    this.realtime.notifications$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((notification) => {
        if (notification.sessionId !== this.id()) return;

        if (
          notification.eventType === 'SessionChanged' ||
          notification.eventType === 'HelpRequestStarted' ||
          notification.eventType === 'HelpRequestAnswered'
        ) {
          const wasOurs =
            notification.helpRequestId !== null &&
            this.requests.value().some((request) => request.id === notification.helpRequestId);

          if (
            notification.eventType === 'HelpRequestAnswered' &&
            wasOurs &&
            notification.helpRequestId !== null
          ) {
            this.answeredRequestId.set(notification.helpRequestId);
          }

          this.queue.reload();
          this.requests.reload();
        }

        if (notification.eventType === 'FaqAnswered') {
          this.queue.reload();
          this.requests.reload();
        }

        if (notification.eventType === 'FaqCreated') {
          this.requests.reload();
        }
      });
  }

  protected backToSession(): void {
    this.router.navigate(['..'], { relativeTo: this.route });
  }

  protected cancelRequest(): void {
    this.cancelError.set(null);
    this.cancelDialogOpen.set(true);
  }

  protected closeCancelDialog(): void {
    if (this.cancelling()) return;
    this.cancelDialogOpen.set(false);
    this.cancelError.set(null);
  }

  protected sendFollowUp(text: string): void {
    const requestId = this.answeredRequestId();
    if (requestId === null || this.sendingMessage()) return;

    this.sendingMessage.set(true);
    this.sendError.set(null);

    this.helpRequestApi.addStudentMessage(requestId, text).subscribe({
      next: () => {
        this.sendingMessage.set(false);
        this.answeredRequestId.set(null);
        this.queue.reload();
        this.requests.reload();
      },
      error: () => {
        this.sendingMessage.set(false);
        this.sendError.set('Kunne ikke sende beskeden. Prøv igen.');
      },
    });
  }

  protected closeChat(): void {
    this.answeredRequestId.set(null);
    this.backToSession();
  }

  protected readonly sendingMessage = signal(false);
  protected readonly sendError = signal<string | null>(null);

  protected readonly answeredRequestId = signal<string | null>(null);

  protected respondToFaq(accept: boolean): void {
    const request = this.activeRequest();
    if (request === null || this.responding()) return;

    this.responding.set(true);
    this.respondError.set(null);

    this.helpRequestApi.respondToFaq(request.id, accept).subscribe({
      next: () => {
        this.responding.set(false);
        if (accept) {
          this.backToSession();
        } else {
          this.queue.reload();
          this.requests.reload();
        }
      },
      error: () => {
        this.responding.set(false);
        this.respondError.set('Kunne ikke svare på FAQ-forslaget. Prøv igen.');
      },
    });
  }

  protected confirmCancel(): void {
    const request = this.activeRequest();
    if (request === null || this.cancelling()) return;

    this.cancelling.set(true);
    this.cancelError.set(null);

    this.helpRequestApi.cancelRequest(request.id).subscribe({
      next: () => {
        this.cancelling.set(false);
        this.cancelDialogOpen.set(false);
        this.queue.reload();
        this.backToSession();
      },
      error: () => {
        this.cancelling.set(false);
        this.cancelError.set('Kunne ikke trække dig ud af køen. Prøv igen.');
      },
    });
  }
}
