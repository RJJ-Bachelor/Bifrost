import { Component, computed, inject, signal } from '@angular/core';
import { Topbar } from '../../../../core/layout/topbar/topbar';
import { Card } from '../../../../shared/components/card/card';
import { Dialog } from '../../../../shared/components/dialog/dialog';
import { SessionList } from '../../../session/components/session-list/session-list';
import { SessionJoinForm } from '../../../session/components/session-join-form/session-join-form';
import { SessionCreateForm } from '../../../session/components/session-create-form/session-create-form';
import { StatusDialog } from '../../../../shared/components/status-dialog/status-dialog';
import { SessionApi } from '../../../session/services/session-api';

@Component({
  imports: [
    Topbar,
    Card,
    Dialog,
    SessionList,
    SessionJoinForm,
    SessionCreateForm,
    StatusDialog,
  ],
  selector: 'bifrost-teacher-sessions',
  styleUrl: './teacher-sessions.scss',
  templateUrl: './teacher-sessions.html',
})
export class TeacherSessions {
  private readonly sessionApi = inject(SessionApi);

  protected readonly sessions = this.sessionApi.getTeacherSessions();

  protected readonly joinDialogOpen = signal(false);
  protected readonly joinError = signal<string | null>(null);
  protected readonly createDialogOpen = signal(false);
  protected readonly createError = signal<string | null>(null);
  protected readonly createSuccess = signal(false);
  protected readonly lastCreatedTitle = signal('');

  protected readonly createSuccessMessage = computed(
    () => `Sessionen "${this.lastCreatedTitle()}" er oprettet.`,
  );

  protected readonly isEmpty = computed(
    () => this.sessions.value().length === 0,
  );

  protected openJoinDialog(): void {
    this.joinDialogOpen.set(true);
    this.joinError.set(null);
  }

  protected closeJoinDialog(): void {
    this.joinDialogOpen.set(false);
    this.joinError.set(null);
  }

  protected openCreateDialog(): void {
    this.createDialogOpen.set(true);
    this.createError.set(null);
    this.createSuccess.set(false);
  }

  protected closeCreateDialog(): void {
    this.createDialogOpen.set(false);
    this.createError.set(null);
    this.createSuccess.set(false);
  }

  protected onJoin(code: string): void {
    this.sessionApi.joinTeacherSession(code).subscribe({
      next: () => {
        this.closeJoinDialog();
        this.sessions.reload();
      },
      error: () => {
        this.joinError.set('Forkert kode – prøv igen');
      },
    });
  }

  protected onCreate(title: string): void {
    this.sessionApi.createSession(title).subscribe({
      next: () => {
        this.createDialogOpen.set(false);
        this.lastCreatedTitle.set(title);
        this.createSuccess.set(true);
        this.sessions.reload();
      },
      error: () => {
        this.createError.set('Kunne ikke oprette sessionen. Prøv igen.');
      },
    });
  }

  protected closeSuccessDialog(): void {
    this.createSuccess.set(false);
  }
}
