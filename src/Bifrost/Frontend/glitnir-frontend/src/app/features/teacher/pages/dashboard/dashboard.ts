import {Component, DestroyRef, ElementRef, afterRenderEffect, computed, inject, input, signal, viewChild,} from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { map, of, switchMap, timer } from 'rxjs';
import { Topbar } from '../../../../core/layout/topbar/topbar';
import { RealtimeHub } from '../../../../core/realtime/services/realtime-hub';
import { Card } from '../../../../shared/components/card/card';
import { StatTile } from '../../../../shared/components/stat-tile/stat-tile';
import { FaqApi, FaqCard } from '../../../faq';
import { HelpRequestApi } from '../../../help-request';
import { SessionApi } from '../../../session';

@Component({
  imports: [Topbar, Card, StatTile, FaqCard],
  selector: 'bifrost-dashboard',
  styleUrl: './dashboard.scss',
  templateUrl: './dashboard.html',
})
export class Dashboard {
  private readonly sessionApi = inject(SessionApi);
  private readonly helpRequestApi = inject(HelpRequestApi);
  private readonly faqApi = inject(FaqApi);
  private readonly realtime = inject(RealtimeHub);
  private readonly destroyRef = inject(DestroyRef);

  readonly id = input.required<string>();

  protected readonly session = this.sessionApi.getTeacherSession(() => this.id());
  protected readonly queue = this.helpRequestApi.getTeacherQueue(() => this.id());
  protected readonly statistics = this.sessionApi.getTeacherStatistics(() => this.id());
  protected readonly faqs = this.faqApi.getTeacherFaqs(() => this.id());

  private readonly now = toSignal(
    toObservable(this.queue.value).pipe(
      switchMap(() => {
        const queuedAt = this.queue.value().frontQueuedAt;
        const queuedAtMs = queuedAt === null ? null : new Date(queuedAt).getTime();
        if (queuedAtMs === null || Number.isNaN(queuedAtMs)) {
          return of(Date.now());
        }
        const msToNextMinute = 60000 - ((Date.now() - queuedAtMs) % 60000);
        return timer(msToNextMinute, 60000).pipe(map(() => Date.now()));
      }),
    ),
    { initialValue: Date.now() },
  );

  protected readonly frontWaitMinutes = computed(() => {
    const queuedAt = this.queue.value().frontQueuedAt;
    if (queuedAt === null) return null;
    const queuedAtMs = new Date(queuedAt).getTime();
    if (Number.isNaN(queuedAtMs)) return null;
    return Math.max(0, Math.floor((this.now() - queuedAtMs) / 60_000));
  });

  protected readonly averageWaitLabel = computed(() => {
    const minutes = this.statistics.value().averageWaitMinutes;
    return minutes === null ? '-' : `${Math.round(minutes)} min`;
  });

  // AI-generated: viewport-fit system (faqListRef through faqResizeObserver + afterRenderEffect below).
  protected readonly faqListRef = viewChild<ElementRef<HTMLElement>>('faqList');
  protected readonly mainRef = viewChild<ElementRef<HTMLElement>>('mainRef');
  protected readonly queueRef = viewChild<ElementRef<HTMLElement>>('queueRef');
  protected readonly faqVisibleCount = signal(Number.MAX_SAFE_INTEGER);
  private faqResizeObserver: ResizeObserver | null = null;

  constructor() {
    this.realtime.notifications$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((notification) => {
        if (notification.sessionId !== this.id()) return;

        if (
          notification.eventType === 'SessionChanged' ||
          notification.eventType === 'HelpRequestCreated' ||
          notification.eventType === 'StudentMessageAdded'
        ) {
          this.queue.reload();
        }

        if (notification.eventType === 'SessionChanged') {
          if (
            notification.change === 'Answered' ||
            notification.change === 'Completed' ||
            notification.change === 'FaqAccepted'
          ) {
            this.statistics.reload();
          }

          if (notification.change === 'FaqAnswered') {
            this.faqs.reload();
          }

          if (notification.change === 'Cleared') {
            this.session.reload();
            this.faqs.reload();
            this.statistics.reload();
          }

          if (notification.change === 'Archived') {
            this.session.reload();
            this.faqs.reload();
            this.statistics.reload();
          }
        }

        if (notification.eventType === 'FaqCreated') {
          this.faqs.reload();
        }
      });

    // AI-generated: re-runs recomputeFaqFit whenever FAQ data, window size, or queue-tile height changes.
    afterRenderEffect(() => {
      this.faqs.value();
      const mainEl = this.mainRef()?.nativeElement;
      const queueEl = this.queueRef()?.nativeElement;
      if (!mainEl || !queueEl) return;

      this.faqResizeObserver?.disconnect();
      this.faqResizeObserver = new ResizeObserver(() => this.recomputeFaqFit());
      this.faqResizeObserver.observe(mainEl);
      this.faqResizeObserver.observe(queueEl);
      this.recomputeFaqFit();
    });

    this.destroyRef.onDestroy(() => this.faqResizeObserver?.disconnect());
  }

  // AI-generated: dynamic fit so projector view shows as many FAQ cards as the viewport allows.
  private recomputeFaqFit(): void {
    const mainEl = this.mainRef()?.nativeElement;
    const listEl = this.faqListRef()?.nativeElement;
    if (!mainEl || !listEl) return;

    const availableHeight = mainEl.getBoundingClientRect().bottom - listEl.getBoundingClientRect().top;

    this.faqVisibleCount.set(Number.MAX_SAFE_INTEGER);
    requestAnimationFrame(() => {
      const cards = Array.from(listEl.querySelectorAll('bifrost-faq-card')) as HTMLElement[];
      const cardGap = parseFloat(getComputedStyle(listEl).rowGap) || 0;
      let usedHeight = 0;
      let count = 0;
      for (const card of cards) {
        const cardHeight = card.offsetHeight;
        if (cardHeight === 0) continue;
        const nextHeight = usedHeight + (count === 0 ? 0 : cardGap) + cardHeight;
        if (nextHeight > availableHeight) break;
        usedHeight = nextHeight;
        count++;
      }
      if (count !== this.faqVisibleCount()) {
        this.faqVisibleCount.set(count);
      }
    });
  }
}
