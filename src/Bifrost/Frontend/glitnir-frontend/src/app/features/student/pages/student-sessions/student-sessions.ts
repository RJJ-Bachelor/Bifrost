import { Component, inject } from '@angular/core';
import { Topbar } from '../../../../core/layout/topbar/topbar';
import { Card } from '../../../../shared/components/card/card';
import { SessionList } from '../../../session/components/session-list/session-list';
import { SessionApi } from '../../../session/services/session-api';

@Component({
  imports: [Topbar, Card, SessionList],
  selector: 'bifrost-student-sessions',
  styleUrl: './student-sessions.scss',
  templateUrl: './student-sessions.html',
})
export class StudentSessions {
  private readonly sessionApi = inject(SessionApi);
  readonly sessions = this.sessionApi.getStudentSessions();
}
