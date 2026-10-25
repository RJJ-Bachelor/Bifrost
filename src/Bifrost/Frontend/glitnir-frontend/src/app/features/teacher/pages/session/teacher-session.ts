import { Component, computed, inject, input } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Topbar } from '../../../../core/layout/topbar/topbar';
import { SessionApi } from '../../../session/services/session-api';

@Component({
  imports: [Topbar],
  selector: 'bifrost-teacher-session',
  styleUrl: './teacher-session.scss',
  templateUrl: './teacher-session.html',
})
export class TeacherSession {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly sessionApi = inject(SessionApi);

  readonly id = input.required<string>();

  protected readonly session = this.sessionApi.getTeacherSession(() => this.id());
  protected readonly sessionTitle = computed(() => this.session.value()?.title ?? '');

  protected switchSession(): void {
    this.router.navigateByUrl('/teacher');
  }

  protected openDashboard(): void {
    this.router.navigate(['dashboard'], { relativeTo: this.route });
  }
}
