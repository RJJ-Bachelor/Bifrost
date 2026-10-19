import { Component } from '@angular/core';
import { Topbar } from '../../../../core/layout/topbar/topbar';
import { Card } from '../../../../shared/components/card/card';
import { SessionList } from '../../../session/components/session-list/session-list';

@Component({
  imports: [Topbar, Card, SessionList],
  selector: 'bifrost-student-sessions',
  styleUrl: './student-sessions.scss',
  templateUrl: './student-sessions.html',
})
export class StudentSessions {}
