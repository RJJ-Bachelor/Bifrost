import { CommonModule } from '@angular/common';
import { Component, inject } from '@angular/core';
import { PushNotificationService } from './push-notification.service';

@Component({
  selector: 'app-push-notifications',
  imports: [CommonModule],
  templateUrl: './push-notifications.component.html'
})
export class PushNotificationsComponent {
  readonly push = inject(PushNotificationService);
}
