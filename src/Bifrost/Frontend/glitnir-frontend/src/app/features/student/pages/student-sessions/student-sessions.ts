import { Component, computed, inject, signal } from '@angular/core';
import { Topbar } from '../../../../core/layout/topbar/topbar';
import { Card } from '../../../../shared/components/card/card';
import { SessionList } from '../../../session/components/session-list/session-list';
import { SessionJoinForm } from '../../../session/components/session-join-form/session-join-form';
import { SessionApi } from '../../../session/services/session-api';

@Component({
  imports: [Topbar, Card, SessionList, SessionJoinForm],
  selector: 'bifrost-student-sessions',
  styleUrl: './student-sessions.scss',
  templateUrl: './student-sessions.html',
})
export class StudentSessions {
  private readonly sessionApi = inject(SessionApi);

  protected readonly sessions = this.sessionApi.getStudentSessions();
  protected readonly userOpenedJoin = signal(false);
  protected readonly submitting = signal(false);
  protected readonly joinError = signal<string | null>(null);

  protected readonly isEmpty = computed(
    () => this.sessions.value().length === 0,
  );

  protected toggleJoinForm(): void {
    this.userOpenedJoin.update((v) => !v);
    this.joinError.set(null);
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
