import { Component, computed, inject, input, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { FormField, form, validate } from '@angular/forms/signals';
import { AuthStore } from '../../../../core/auth/services/auth-store';
import { Topbar } from '../../../../core/layout/topbar/topbar';
import { Card } from '../../../../shared/components/card/card';
import { Dialog } from '../../../../shared/components/dialog/dialog';
import { StatTile } from '../../../../shared/components/stat-tile/stat-tile';
import { StatusDialog } from '../../../../shared/components/status-dialog/status-dialog';
import { FaqArchiveList } from '../../../faq/components/faq-archive-list/faq-archive-list';
import { FaqList } from '../../../faq/components/faq-list/faq-list';
import { FaqItem } from '../../../faq/faq-item.model';
import { FaqApi } from '../../../faq/services/faq-api';
import { HelpRequestApi } from '../../../help-request/services/help-request-api';
import { SessionApi } from '../../../session/services/session-api';
import { SessionGeneration } from '../../../session/session.model';

interface FaqFormModel {
  question: string;
  answer: string;
}

@Component({
  imports: [Topbar, Card, StatTile, FaqList, FaqArchiveList, Dialog, StatusDialog, FormField],
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
  protected readonly frontWaitMinutes = computed(() => {
    const queuedAt = this.queue.value().frontQueuedAt;
    if (queuedAt === null) return null;
    return Math.max(0, Math.floor((Date.now() - new Date(queuedAt).getTime()) / 60000));
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
  protected readonly renameModel = signal('');
  protected readonly renaming = signal(false);
  protected readonly renameError = signal<string | null>(null);
  protected readonly deleteConfirmOpen = signal(false);
  protected readonly deleting = signal(false);
  protected readonly archiveDialogOpen = signal(false);
  protected readonly archiveTitle = signal('');
  protected readonly archiving = signal(false);
  protected readonly archiveError = signal<string | null>(null);
  protected readonly faqs = this.faqApi.getTeacherFaqs(() => this.id());
  protected readonly faqDialogOpen = signal(false);
  protected readonly faqEditTarget = signal<FaqItem | null>(null);
  protected readonly faqSubmitting = signal(false);
  protected readonly faqDialogError = signal<string | null>(null);
  protected readonly faqDeleteTarget = signal<FaqItem | null>(null);
  protected readonly faqDeleting = signal(false);
  protected readonly faqStatusOpen = signal(false);
  protected readonly faqStatusMessage = signal('');

  protected readonly faqModel = signal<FaqFormModel>({ question: '', answer: '' });
  protected readonly faqForm = form(this.faqModel, (path) => {
    validate(path.question, ({ value }) =>
      value().trim().length === 0
        ? { kind: 'required', message: 'Skriv et spørgsmål' }
        : null,
    );
    validate(path.answer, ({ value }) =>
      value().trim().length === 0
        ? { kind: 'required', message: 'Skriv et svar' }
        : null,
    );
  });

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
    this.faqEditTarget.set(null);
    this.faqModel.set({ question: '', answer: '' });
    this.faqDialogError.set(null);
    this.faqDialogOpen.set(true);
  }

  protected openEditFaq(item: FaqItem): void {
    this.faqEditTarget.set(item);
    this.faqModel.set({ question: item.question, answer: item.answer });
    this.faqDialogError.set(null);
    this.faqDialogOpen.set(true);
  }

  protected closeFaqDialog(): void {
    this.faqDialogOpen.set(false);
    this.faqDialogError.set(null);
  }

  protected submitFaq(event: Event): void {
    event.preventDefault();
    if (this.faqForm().invalid() || this.faqSubmitting()) return;

    this.faqSubmitting.set(true);
    this.faqDialogError.set(null);

    const { question, answer } = this.faqModel();
    const target = this.faqEditTarget();
    const request$ = target
      ? this.faqApi.updateFaq(target.id, question, answer)
      : this.faqApi.createFaq(this.id(), question, answer);

    request$.subscribe({
      next: () => {
        this.faqSubmitting.set(false);
        this.faqDialogOpen.set(false);
        this.showFaqStatus(target ? 'FAQ opdateret.' : 'FAQ oprettet.');
        this.faqs.reload();
      },
      error: () => {
        this.faqSubmitting.set(false);
        this.faqDialogError.set('Kunne ikke gemme FAQ. Prøv igen.');
      },
    });
  }

  protected requestDeleteFaq(item: FaqItem): void {
    this.faqDialogError.set(null);
    this.faqDeleteTarget.set(item);
  }

  protected cancelDeleteFaq(): void {
    this.faqDeleteTarget.set(null);
    this.faqDialogError.set(null);
  }

  protected confirmDeleteFaq(): void {
    const target = this.faqDeleteTarget();
    if (target === null || this.faqDeleting()) return;

    this.faqDeleting.set(true);
    this.faqApi.deleteFaq(target.id).subscribe({
      next: () => {
        this.faqDeleting.set(false);
        this.faqDeleteTarget.set(null);
        this.showFaqStatus('FAQ slettet.');
        this.faqs.reload();
      },
      error: () => {
        this.faqDeleting.set(false);
        this.faqDialogError.set('Kunne ikke slette FAQ. Prøv igen.');
      },
    });
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
    this.renameModel.set(this.session.value()?.title ?? '');
    this.renameError.set(null);
    this.settingsOpen.set(true);
  }

  protected closeSettings(): void {
    this.settingsOpen.set(false);
    this.renameError.set(null);
  }

  protected onRenameInput(event: Event): void {
    this.renameModel.set((event.target as HTMLInputElement).value);
  }

  protected submitRename(event: Event): void {
    event.preventDefault();
    const title = this.renameModel().trim();
    if (title.length === 0 || this.renaming()) return;
    // TODO(BF-47): call rename endpoint when backend supports it.
    this.renaming.set(true);
    this.renameError.set(null);
    setTimeout(() => {
      this.renaming.set(false);
    }, 200);
  }

  protected openDeleteConfirm(): void {
    this.settingsOpen.set(false);
    this.deleteConfirmOpen.set(true);
  }

  protected closeDeleteConfirm(): void {
    this.deleteConfirmOpen.set(false);
  }

  protected confirmDelete(): void {
    if (this.deleting()) return;

    this.deleting.set(true);
    this.sessionApi.deleteSession(this.id()).subscribe({
      next: () => {
        this.deleting.set(false);
        this.deleteConfirmOpen.set(false);
        this.router.navigateByUrl('/teacher');
      },
      error: () => {
        this.deleting.set(false);
      },
    });
  }

  protected openArchiveConfirm(): void {
    this.archiveTitle.set('');
    this.archiveError.set(null);
    this.archiveDialogOpen.set(true);
  }

  protected closeArchiveConfirm(): void {
    this.archiveDialogOpen.set(false);
    this.archiveError.set(null);
  }

  protected confirmArchive(): void {
    const title = this.archiveTitle().trim();
    if (title.length === 0 || this.archiving()) return;

    const generation = this.session.value()?.currentGeneration;
    if (generation === undefined) return;

    this.archiving.set(true);
    this.archiveError.set(null);

    this.sessionApi.archiveSession(this.id(), generation, title).subscribe({
      next: () => {
        this.archiving.set(false);
        this.archiveDialogOpen.set(false);
        this.session.reload();
        this.queue.reload();
        this.faqs.reload();
        this.archives.reload();
      },
      error: () => {
        this.archiving.set(false);
        this.archiveError.set('Kunne ikke arkivere. Prøv igen.');
      },
    });
  }

  private showFaqStatus(message: string): void {
    this.faqStatusMessage.set(message);
    this.faqStatusOpen.set(true);
  }
}
