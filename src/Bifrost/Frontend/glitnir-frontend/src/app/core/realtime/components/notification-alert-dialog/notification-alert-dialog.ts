import {Component, inject} from '@angular/core';
import {Dialog} from '../../../../shared/components/dialog/dialog';
import {NotificationAlertService} from '../../services/notification-alert.service';

@Component({
  selector: 'bifrost-notification-alert-dialog',
  imports: [Dialog],
  templateUrl: './notification-alert-dialog.html',
  styleUrl: './notification-alert-dialog.scss',
})
export class NotificationAlertDialog {
  protected readonly alertService = inject(NotificationAlertService);
}
