import { Component, computed, inject, input, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Topbar } from '../../../../core/layout/topbar/topbar';
import { Card } from '../../../../shared/components/card/card';
import { StatTile } from '../../../../shared/components/stat-tile/stat-tile';
import { HelpRequestApi } from '../../../help-request/services/help-request-api';
import { SessionApi } from '../../../session/services/session-api';

@Component({
  imports: [Topbar, Card, StatTile],
  selector: 'bifrost-teacher-session',
  styleUrl: './teacher-session.scss',
  templateUrl: './teacher-session.html',
})
export class TeacherSession {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly sessionApi = inject(SessionApi);
  private readonly helpRequestApi = inject(HelpRequestApi);

  readonly id = input.required<string>();

  protected readonly session = this.sessionApi.getTeacherSession(() => this.id());
  protected readonly sessionTitle = computed(() => this.session.value()?.title ?? '');
  protected readonly queue = this.helpRequestApi.getTeacherQueue(() => this.id());
  protected readonly frontWaitMinutes = computed(() => {
    const queuedAt = this.queue.value().frontQueuedAt;
    if (queuedAt === null) return null;
    return Math.max(0, Math.floor((Date.now() - new Date(queuedAt).getTime()) / 60000));
  });
  protected readonly taking = signal(false);
  protected readonly takeError = signal<string | null>(null);

  protected switchSession(): void {
    this.router.navigateByUrl('/teacher');
  }

  protected openDashboard(): void {
    this.router.navigate(['dashboard'], { relativeTo: this.route });
  }

  protected takeNext(): void {
    if (this.taking() || this.queue.value().waitingCount === 0) return;

    this.taking.set(true);
    this.takeError.set(null);

    this.helpRequestApi.takeNext(this.id()).subscribe({
      next: (request) => {
        this.taking.set(false);
        if (request === null) {
          this.queue.reload();
          return;
        }
        this.router.navigate(['answer', request.id], { relativeTo: this.route });
      },
      error: () => {
        this.taking.set(false);
        this.takeError.set('Kunne ikke hente næste spørgsmål. Prøv igen.');
      },
    });
  }
}
