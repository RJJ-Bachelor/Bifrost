import { Component, computed, inject, input, output } from '@angular/core';
import { DomSanitizer } from '@angular/platform-browser';
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

  private readonly sanitizer = inject(DomSanitizer);

  protected readonly icon = computed(() => {
    const shape =
      this.variant() === 'success'
        ? `<circle cx="12" cy="12" r="11" fill="currentColor"/>
           <path d="M7 12.5 L10.5 16 L17 8.5" stroke="white" stroke-width="2" fill="none" stroke-linecap="round" stroke-linejoin="round"/>`
        : `<circle cx="12" cy="12" r="11" fill="currentColor"/>
           <path d="M12 7 L12 14" stroke="white" stroke-width="2" fill="none" stroke-linecap="round"/>
           <circle cx="12" cy="17.5" r="1.25" fill="white"/>`;

    return this.sanitizer.bypassSecurityTrustHtml(
      `<svg viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg" width="100%" height="100%">${shape}</svg>`,
    );
  });
}
