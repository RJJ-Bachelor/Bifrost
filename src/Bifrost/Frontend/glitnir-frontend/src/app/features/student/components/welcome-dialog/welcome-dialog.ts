import {Component, signal} from '@angular/core';
import {Dialog} from '../../../../shared/components/dialog/dialog';

const STORAGE_KEY = 'bifrost-welcome-seen';

@Component({
  selector: 'bifrost-welcome-dialog',
  imports: [Dialog],
  templateUrl: './welcome-dialog.html',
  styleUrl: './welcome-dialog.scss',
})
export class WelcomeDialog {
  readonly open = signal(!this.hasSeenBefore());

  protected dismiss(): void {
    this.open.set(false);
    try {
      localStorage.setItem(STORAGE_KEY, 'true');
    } catch {
      // Ignore storage errors in restricted browser contexts.
    }
  }

  private hasSeenBefore(): boolean {
    try {
      return localStorage.getItem(STORAGE_KEY) === 'true';
    } catch {
      return false;
    }
  }
}
