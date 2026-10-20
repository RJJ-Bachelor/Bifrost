import { Component, computed, inject, signal } from '@angular/core';
import { Topbar } from '../../../../core/layout/topbar/topbar';
import { Card } from '../../../../shared/components/card/card';
import { Dialog } from '../../../../shared/components/dialog/dialog';
import { SessionList } from '../../../session/components/session-list/session-list';
import { SessionJoinForm } from '../../../session/components/session-join-form/session-join-form';
import { SessionApi } from '../../../session/services/session-api';

@Component({
  imports: [Topbar, Card, Dialog, SessionList, SessionJoinForm],
  selector: 'bifrost-teacher-sessions',
  styleUrl: './teacher-sessions.scss',
  templateUrl: './teacher-sessions.html',
})
export class TeacherSessions {
  private readonly sessionApi = inject(SessionApi);

  protected readonly sessions = this.sessionApi.getTeacherSessions();
  protected readonly joinDialogOpen = signal(false);
  protected readonly joinError = signal<string | null>(null);

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

  protected onJoin(code: string): void {
    console.log('TODO: teacher join endpoint mangler', code);
    this.closeJoinDialog();
  }
}
