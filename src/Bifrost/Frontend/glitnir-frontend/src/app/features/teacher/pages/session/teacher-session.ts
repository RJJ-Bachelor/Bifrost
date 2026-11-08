import { Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { map, of, switchMap, timer } from 'rxjs';
import { Topbar } from '../../../../core/layout/topbar/topbar';
import { RealtimeHub } from '../../../../core/realtime/services/realtime-hub';
import { Card } from '../../../../shared/components/card/card';
import { StatTile } from '../../../../shared/components/stat-tile/stat-tile';
import { StatusDialog } from '../../../../shared/components/status-dialog/status-dialog';
import {
  FaqApi,
  FaqArchiveList,
  FaqFormDialog,
  FaqList,
  type FaqDialogState,
} from '../../../faq';
import { HelpRequestApi } from '../../../help-request';
import { SessionApi, type SessionGeneration } from '../../../session';
import { ArchiveConfirmDialog } from './components/archive-confirm-dialog/archive-confirm-dialog';
import { SessionDeleteDialog } from './components/session-delete-dialog/session-delete-dialog';
import { SessionResetDialog } from './components/session-reset-dialog/session-reset-dialog';
import { SessionSettingsDialog } from './components/session-settings-dialog/session-settings-dialog';

@Component({
  imports: [Topbar, Card, StatTile, FaqList, FaqFormDialog, FaqArchiveList, ArchiveConfirmDialog, SessionSettingsDialog, SessionDeleteDialog, SessionResetDialog, StatusDialog],
  selector: 'bifrost-teacher-session',
  styleUrl: './teacher-session.scss',
  templateUrl: './teacher-session.html',
})
export class TeacherSession {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly sessionApi = inject(SessionApi);
  private readonly helpRequestApi = inject(HelpRequestApi);
  private readonly faqApi = inject(FaqApi);
  private readonly realtime = inject(RealtimeHub);
  private readonly destroyRef = inject(DestroyRef);

  readonly id = input.required<string>();

  protected readonly session = this.sessionApi.getTeacherSession(() => this.id());
  protected readonly sessionTitle = computed(() => this.session.value()?.title ?? '');
  protected readonly queue = this.helpRequestApi.getTeacherQueue(() => this.id());

  // Emits at every whole-minute boundary since the front request was queued,
  // so frontWaitMinutes updates exactly when the displayed minute changes.
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
  protected readonly myReservedRequestId = computed(() => this.queue.value().myReservedRequestId);
  protected readonly taking = signal(false);
  protected readonly takeError = signal<string | null>(null);

  protected readonly statistics = this.sessionApi.getTeacherStatistics(() => this.id());
  protected readonly averageWaitLabel = computed(() => {
    const minutes = this.statistics.value().averageWaitMinutes;
    return minutes === null ? '-' : `${Math.round(minutes)} min`;
  });
  protected readonly archives = this.sessionApi.getTeacherArchives(() => this.id());
  protected readonly settingsOpen = signal(false);
  protected readonly deleteConfirmOpen = signal(false);
  protected readonly resetConfirmOpen = signal(false);
  protected readonly archiveDialogOpen = signal(false);
  protected readonly faqs = this.faqApi.getTeacherFaqs(() => this.id());
  protected readonly faqDialogState = signal<FaqDialogState>({ mode: 'closed' });
  protected readonly faqStatusOpen = signal(false);
  protected readonly faqStatusMessage = signal('');

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
          if (notification.change === 'Answered' || notification.change === 'Completed' || notification.change === 'FaqAccepted') {
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
            this.archives.reload();
          }
        }

        if (notification.eventType === 'FaqCreated') {
          this.faqs.reload();
        }
      });
  }

  protected switchSession(): void {
    this.router.navigateByUrl('/teacher');
  }

  protected openDashboard(): void {
    const urlTree = this.router.createUrlTree(['dashboard'], { relativeTo: this.route });
    window.open(this.router.serializeUrl(urlTree), '_blank', 'noopener,width=1200,height=900');
  }

  protected resumeOwnRequest(): void {
    const id = this.myReservedRequestId();
    if (id === null) return;
    this.router.navigate(['answer', id], { relativeTo: this.route });
  }

  protected takeNext(): void {
    if (this.taking() || this.queue.value().unreservedWaitingCount === 0) return;

    this.taking.set(true);
    this.takeError.set(null);

    this.helpRequestApi.takeNext(this.id()).subscribe({
      next: (request) => {
        this.taking.set(false);
        if (request === null) {
          this.queue.reload();
          return;
        }
        this.router.navigate(['answer', request.id], { relativeTo: this.route });
      },
      error: () => {
        this.taking.set(false);
        this.takeError.set('Kunne ikke hente næste spørgsmål. Prøv igen.');
      },
    });
  }

  protected openCreateFaq(): void {
    this.faqDialogState.set({ mode: 'create' });
  }

  protected closeFaqDialog(): void {
    this.faqDialogState.set({ mode: 'closed' });
  }

  protected onFaqSaved(): void {
    this.showFaqStatus('FAQ oprettet.');
    this.faqDialogState.set({ mode: 'closed' });
    this.faqs.reload();
  }

  protected closeFaqStatus(): void {
    this.faqStatusOpen.set(false);
  }

  protected onArchiveSelected(archive: SessionGeneration): void {
    this.router.navigate(['archives', archive.id], { relativeTo: this.route });
  }

  protected openSettings(): void {
    this.settingsOpen.set(true);
  }

  protected closeSettings(): void {
    this.settingsOpen.set(false);
  }

  protected onSessionRenamed(): void {
    this.settingsOpen.set(false);
    this.session.reload();
  }

  protected onDeleteRequested(): void {
    this.settingsOpen.set(false);
    this.deleteConfirmOpen.set(true);
  }

  protected closeDeleteConfirm(): void {
    this.deleteConfirmOpen.set(false);
    this.settingsOpen.set(true);
  }

  protected onSessionDeleted(): void {
    this.deleteConfirmOpen.set(false);
    this.router.navigateByUrl('/teacher');
  }

  protected onResetRequested(): void {
    this.settingsOpen.set(false);
    this.resetConfirmOpen.set(true);
  }

  protected closeResetConfirm(): void {
    this.resetConfirmOpen.set(false);
    this.settingsOpen.set(true);
  }

  protected onSessionCleared(): void {
    this.resetConfirmOpen.set(false);
    this.session.reload();
    this.queue.reload();
    this.faqs.reload();
    this.statistics.reload();
    this.archives.reload();
  }

  protected openArchiveConfirm(): void {
    this.archiveDialogOpen.set(true);
  }

  protected closeArchiveConfirm(): void {
    this.archiveDialogOpen.set(false);
  }

  protected onArchived(): void {
    this.archiveDialogOpen.set(false);
    this.session.reload();
    this.queue.reload();
    this.faqs.reload();
    this.archives.reload();
    this.statistics.reload();
  }

  private showFaqStatus(message: string): void {
    this.faqStatusMessage.set(message);
    this.faqStatusOpen.set(true);
  }
}
