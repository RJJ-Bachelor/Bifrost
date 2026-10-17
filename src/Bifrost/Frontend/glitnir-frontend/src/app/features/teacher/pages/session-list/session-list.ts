import { Component } from '@angular/core';
import { Topbar } from '../../../../core/layout/topbar/topbar';

@Component({
  imports: [Topbar],
  selector: 'bifrost-teacher-session-list',
  styleUrl: './session-list.scss',
  templateUrl: './session-list.html',
})
export class SessionList {}
