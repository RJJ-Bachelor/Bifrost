import {DestroyRef, Service, inject, signal} from '@angular/core';
import {takeUntilDestroyed} from '@angular/core/rxjs-interop';
import {Router} from '@angular/router';
import {AuthStore} from '../../auth/services/auth-store';
import {NotificationEventType, type NotificationMessage} from '../notification.model';
import {RealtimeHub} from './realtime-hub';

@Service()
export class NotificationAlertService {
  private readonly realtime = inject(RealtimeHub);
  private readonly router = inject(Router);
  private readonly authStore = inject(AuthStore);
  private readonly destroyRef = inject(DestroyRef);

  readonly notification = signal<NotificationMessage | null>(null);

  constructor() {
    this.realtime.notifications$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((item) => {
        if (item.deliveryType !== 'RealtimeOrPush') return;
        if (!this.isWindowFocused()) return;
        if (this.isAlreadyOnRelevantPage(item)) return;
        this.notification.set(item);
      });
  }

  dismiss(): void {
    this.notification.set(null);
  }

  goToNotification(): void {
    const current = this.notification();
    this.dismiss();
    if (!current) return;

    const target = this.getTargetRoute(current);
    if (target) {
      void this.router.navigateByUrl(target);
    }
  }

  getActionLabel(item: NotificationMessage | null): string {
    if (!item) return 'Gå til';
    if (this.authStore.role() === 'teacher') {
      return item.sessionId ? 'Gå til session' : 'Gå til oversigt';
    }
    return item.sessionId ? 'Gå til kø' : 'Gå til oversigt';
  }

  protected isWindowFocused(): boolean {
    return document.visibilityState === 'visible' && document.hasFocus();
  }

  private isAlreadyOnRelevantPage(item: NotificationMessage): boolean {
    const path = this.router.url.split('?')[0];
    const role = this.authStore.role();
    const sessionId = item.sessionId;

    if (!sessionId) return false;

    if (role === 'teacher') {
      const isTeacherEvent =
        item.eventType === NotificationEventType.HelpRequestCreated ||
        item.eventType === NotificationEventType.StudentMessageAdded ||
        item.eventType === NotificationEventType.FaqCreated;
      return isTeacherEvent && path === `/teacher/sessions/${sessionId}`;
    }

    const isStudentEvent =
      item.eventType === NotificationEventType.HelpRequestStarted ||
      item.eventType === NotificationEventType.HelpRequestAnswered ||
      item.eventType === NotificationEventType.FaqAnswered;
    return isStudentEvent && path === `/sessions/${sessionId}/queue`;
  }

  private getTargetRoute(item: NotificationMessage): string | null {
    const role = this.authStore.role();
    const sessionId = item.sessionId;

    if (role === 'teacher') {
      return sessionId ? `/teacher/sessions/${sessionId}` : '/teacher';
    }

    return sessionId ? `/sessions/${sessionId}/queue` : '/';
  }
}
