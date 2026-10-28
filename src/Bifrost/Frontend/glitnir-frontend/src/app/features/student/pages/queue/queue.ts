import { Component, DestroyRef, computed, inject, input } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { map, of, switchMap, timer } from 'rxjs';
import { Topbar } from '../../../../core/layout/topbar/topbar';
import { Card } from '../../../../shared/components/card/card';
import { HelpRequestApi, type HelpRequest } from '../../../help-request';
import { SessionApi } from '../../../session';

type QueueViewState = 'in-queue' | 'my-turn' | 'none';

@Component({
  imports: [Topbar, Card],
  selector: 'bifrost-queue',
  styleUrl: './queue.scss',
  templateUrl: './queue.html',
})
export class Queue {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly helpRequestApi = inject(HelpRequestApi);
  private readonly sessionApi = inject(SessionApi);
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
    const request = this.activeRequest();
    if (request === null) return 'none';
    if (request.status === 'active') return 'my-turn';
    return 'in-queue';
  });

  // Second-tick that runs only while the request is pending in the queue.
  private readonly now = toSignal(
    toObservable(this.activeRequest).pipe(
      switchMap((request) => {
        if (request === null || request.status !== 'pending') return of(Date.now());
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

  protected readonly displayNumber = computed(() => this.queue.value().displayNumber ?? '—');
  protected readonly position = computed(() => this.queue.value().position);
  protected readonly estimatedWait = computed(() => this.queue.value().estimatedWaitMinutes);

  protected backToSession(): void {
    this.router.navigate(['..'], { relativeTo: this.route });
  }

  protected cancelRequest(): void {
    // TODO: implement confirm dialog + API call.
  }
}
