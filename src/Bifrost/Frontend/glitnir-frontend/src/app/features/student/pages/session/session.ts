import { Component, computed, inject, input } from '@angular/core';
import { Router } from '@angular/router';
import { Topbar } from '../../../../core/layout/topbar/topbar';
import { Card } from '../../../../shared/components/card/card';
import { SessionApi } from '../../../session/services/session-api';

@Component({
  imports: [Topbar, Card],
  selector: 'bifrost-student-session',
  styleUrl: './session.scss',
  templateUrl: './session.html',
})
export class Session {
  private readonly router = inject(Router);
  private readonly sessionApi = inject(SessionApi);

  readonly id = input.required<string>();

  protected readonly session = this.sessionApi.getStudentSession(() => this.id());
  protected readonly sessionTitle = computed(() => this.session.value()?.title ?? '');

  protected switchSession(): void {
    this.router.navigateByUrl('/');
  }
}
