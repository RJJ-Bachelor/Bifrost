import { Component, computed, inject, input, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthStore } from '../../../../core/auth/services/auth-store';
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
  private readonly authStore = inject(AuthStore);

  readonly id = input.required<string>();

  protected readonly session = this.sessionApi.getTeacherSession(() => this.id());
  protected readonly sessionTitle = computed(() => this.session.value()?.title ?? '');
  protected readonly queue = this.helpRequestApi.getTeacherQueue(() => this.id());
  protected readonly frontWaitMinutes = computed(() => {
    const queuedAt = this.queue.value().frontQueuedAt;
    if (queuedAt === null) return null;
    return Math.max(0, Math.floor((Date.now() - new Date(queuedAt).getTime()) / 60000));
  });
  // TODO(BF-46): replace with backend-provided myReservedRequestId.
  protected readonly myReservedRequestId = computed(() => {
    const me = this.authStore.user()?.userId;
    if (!me) return null;
    const queue = this.queue.value();
    const pending = queue.pendingRequests.find((item) => item.assignedTeacherId === me);
    if (pending) return pending.id;
    const active = queue.activeRequests.find((item) => item.assignedTeacherId === me);
    return active?.id ?? null;
  });
  protected readonly taking = signal(false);
  protected readonly takeError = signal<string | null>(null);

  protected switchSession(): void {
    this.router.navigateByUrl('/teacher');
  }

  protected openDashboard(): void {
    this.router.navigate(['dashboard'], { relativeTo: this.route });
  }

  protected resumeOwnRequest(): void {
    const id = this.myReservedRequestId();
    if (id === null) return;
    this.router.navigate(['answer', id], { relativeTo: this.route });
  }

  protected takeNext(): void {
    if (this.taking() || this.queue.value().availableCount === 0) return;

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
