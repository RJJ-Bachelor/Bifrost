import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthStore } from '../../../../core/auth/services/auth-store';
import { Topbar } from '../../../../core/layout/topbar/topbar';
import { Card } from '../../../../shared/components/card/card';
import { Dialog } from '../../../../shared/components/dialog/dialog';
import { StatusDialog } from '../../../../shared/components/status-dialog/status-dialog';
import {
  SessionApi,
  SessionJoinForm,
  SessionList,
  type Session,
} from '../../../session';
import { IdentityTransferDialog } from './components/identity-transfer-dialog/identity-transfer-dialog';

@Component({
  imports: [Topbar, Card, Dialog, SessionList, SessionJoinForm, IdentityTransferDialog, StatusDialog],
  selector: 'bifrost-student-sessions',
  styleUrl: './student-sessions.scss',
  templateUrl: './student-sessions.html',
})
export class StudentSessions {
  private readonly sessionApi = inject(SessionApi);
  private readonly authStore = inject(AuthStore);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly sessions = this.sessionApi.getStudentSessions();
  protected readonly userOpenedJoin = signal(false);
  protected readonly transferDialogOpen = signal(false);
  protected readonly transferSuccess = signal(false);
  protected readonly submitting = signal(false);
  protected readonly joinError = signal<string | null>(null);

  protected readonly isEmpty = computed(
    () => this.sessions.value().length === 0,
  );

  protected openDialog(): void {
    this.userOpenedJoin.set(true);
    this.joinError.set(null);
  }

  protected closeDialog(): void {
    this.userOpenedJoin.set(false);
    this.joinError.set(null);
  }

  protected openTransferDialog(): void {
    this.transferDialogOpen.set(true);
  }

  protected closeTransferDialog(): void {
    this.transferDialogOpen.set(false);
  }

  protected async refreshAfterTransfer(): Promise<void> {
    this.transferDialogOpen.set(false);
    await this.authStore.loadIdentity('student');
    this.transferSuccess.set(true);
    this.sessions.reload();
  }

  protected closeTransferSuccess(): void {
    this.transferSuccess.set(false);
  }

  protected navigateToSession(session: Session): void {
    this.router.navigate(['sessions', session.id], {
      relativeTo: this.route,
    });
  }

  protected onJoin(code: string): void {
    if (this.submitting()) return;

    this.submitting.set(true);
    this.joinError.set(null);

    this.sessionApi.joinSession(code).subscribe({
      next: () => {
        this.submitting.set(false);
        this.userOpenedJoin.set(false);
        this.sessions.reload();
      },
      error: () => {
        this.submitting.set(false);
        this.joinError.set('Ugyldig kode. Prøv igen');
      },
    });
  }
}
