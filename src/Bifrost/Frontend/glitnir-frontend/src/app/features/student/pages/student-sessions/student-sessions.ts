import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Topbar } from '../../../../core/layout/topbar/topbar';
import { Card } from '../../../../shared/components/card/card';
import { Dialog } from '../../../../shared/components/dialog/dialog';
import {
  SessionApi,
  SessionJoinForm,
  SessionList,
  type Session,
} from '../../../session';

@Component({
  imports: [Topbar, Card, Dialog, SessionList, SessionJoinForm],
  selector: 'bifrost-student-sessions',
  styleUrl: './student-sessions.scss',
  templateUrl: './student-sessions.html',
})
export class StudentSessions {
  private readonly sessionApi = inject(SessionApi);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly sessions = this.sessionApi.getStudentSessions();
  protected readonly userOpenedJoin = signal(false);
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
