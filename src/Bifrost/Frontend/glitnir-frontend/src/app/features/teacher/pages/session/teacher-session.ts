import { Component, computed, inject, input, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { map, of, switchMap, timer } from 'rxjs';
import { AuthStore } from '../../../../core/auth/services/auth-store';
import { Topbar } from '../../../../core/layout/topbar/topbar';
import { Card } from '../../../../shared/components/card/card';
import { Dialog } from '../../../../shared/components/dialog/dialog';
import { StatTile } from '../../../../shared/components/stat-tile/stat-tile';
import { StatusDialog } from '../../../../shared/components/status-dialog/status-dialog';
import {
  FaqApi,
  FaqArchiveList,
  FaqDeleteDialog,
  FaqFormDialog,
  FaqList,
  type FaqDialogState,
  type FaqItem,
  type FaqSavedEvent,
} from '../../../faq';
import { HelpRequestApi } from '../../../help-request';
import { SessionApi, type SessionGeneration } from '../../../session';
import { ArchiveConfirmDialog } from './components/archive-confirm-dialog/archive-confirm-dialog';
import { SessionDeleteDialog } from './components/session-delete-dialog/session-delete-dialog';
import { SessionSettingsDialog } from './components/session-settings-dialog/session-settings-dialog';

@Component({
  imports: [Topbar, Card, StatTile, FaqList, FaqFormDialog, FaqDeleteDialog, FaqArchiveList, ArchiveConfirmDialog, SessionSettingsDialog, SessionDeleteDialog, Dialog, StatusDialog],
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
  private readonly authStore = inject(AuthStore);

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
  // TODO(BF-46): replace with backend-provided myReservedRequestId.
  protected readonly myReservedRequestId = computed(() => {
    const me = this.authStore.user()?.userId;
    if (!me) return null;
    const queue = this.queue.value();
    const pending = queue.pendingRequests.find((item) => item.assignedTeacherId === me);
    if (pending) return pending.id;
    const active = queue.activeRequests.find((item) => item.assignedTeacherId === me);
    return active?.id ?? null;
  });
  protected readonly taking = signal(false);
  protected readonly takeError = signal<string | null>(null);

  protected readonly statistics = this.sessionApi.getTeacherStatistics(() => this.id());
  protected readonly archives = this.sessionApi.getTeacherArchives(() => this.id());
  protected readonly selectedArchive = signal<SessionGeneration | null>(null);
  protected readonly settingsOpen = signal(false);
  protected readonly deleteConfirmOpen = signal(false);
  protected readonly archiveDialogOpen = signal(false);
  protected readonly faqs = this.faqApi.getTeacherFaqs(() => this.id());
  protected readonly faqDialogState = signal<FaqDialogState>({ mode: 'closed' });
  protected readonly faqDeleteTarget = signal<FaqItem | null>(null);
  protected readonly faqStatusOpen = signal(false);
  protected readonly faqStatusMessage = signal('');

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
    if (this.taking() || this.queue.value().availableCount === 0) return;

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

  protected openEditFaq(item: FaqItem): void {
    this.faqDialogState.set({ mode: 'edit', item });
  }

  protected closeFaqDialog(): void {
    this.faqDialogState.set({ mode: 'closed' });
  }

  protected onFaqSaved(event: FaqSavedEvent): void {
    this.showFaqStatus(event.mode === 'edit' ? 'FAQ opdateret.' : 'FAQ oprettet.');
    this.faqDialogState.set({ mode: 'closed' });
    this.faqs.reload();
  }

  protected requestDeleteFaq(item: FaqItem): void {
    this.faqDeleteTarget.set(item);
  }

  protected cancelDeleteFaq(): void {
    this.faqDeleteTarget.set(null);
  }

  protected onFaqDeleted(): void {
    this.faqDeleteTarget.set(null);
    this.showFaqStatus('FAQ slettet.');
    this.faqs.reload();
  }

  protected closeFaqStatus(): void {
    this.faqStatusOpen.set(false);
  }

  protected onArchiveSelected(archive: SessionGeneration): void {
    this.selectedArchive.set(archive);
  }

  protected closeArchiveDialog(): void {
    this.selectedArchive.set(null);
  }

  protected openSettings(): void {
    this.settingsOpen.set(true);
  }

  protected closeSettings(): void {
    this.settingsOpen.set(false);
  }

  protected onDeleteRequested(): void {
    this.settingsOpen.set(false);
    this.deleteConfirmOpen.set(true);
  }

  protected closeDeleteConfirm(): void {
    this.deleteConfirmOpen.set(false);
  }

  protected onSessionDeleted(): void {
    this.deleteConfirmOpen.set(false);
    this.router.navigateByUrl('/teacher');
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
  }

  private showFaqStatus(message: string): void {
    this.faqStatusMessage.set(message);
    this.faqStatusOpen.set(true);
  }
}
