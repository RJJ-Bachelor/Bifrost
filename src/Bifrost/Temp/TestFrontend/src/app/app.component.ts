import { Component, DestroyRef, inject } from '@angular/core';
import { NotificationService } from './notification.service';
import { PushNotificationService } from './notifications/push/push-notification.service';

@Component({
  selector: 'app-root',
  templateUrl: './app.component.html',
  standalone: false,
  styleUrl: './app.component.css'
})
export class AppComponent {
  readonly notifications = inject(NotificationService);
  private readonly pushNotifications = inject(PushNotificationService);

  constructor() {
    inject(DestroyRef).onDestroy(() => { void this.notifications.stop(); });
  }
}
