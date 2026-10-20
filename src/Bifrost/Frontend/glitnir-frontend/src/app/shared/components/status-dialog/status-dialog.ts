import { Component, input, output } from '@angular/core';
import { Dialog } from '../dialog/dialog';

@Component({
  selector: 'bifrost-status-dialog',
  imports: [Dialog],
  templateUrl: './status-dialog.html',
  styleUrl: './status-dialog.scss',
})
export class StatusDialog {
  readonly open = input.required<boolean>();
  readonly message = input.required<string>();
  readonly variant = input<'success' | 'error'>('success');
  readonly closed = output<void>();
}
