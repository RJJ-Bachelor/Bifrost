import { Component, ElementRef, effect, input, output, viewChild } from '@angular/core';

@Component({
  selector: 'bifrost-dialog',
  imports: [],
  templateUrl: './dialog.html',
  styleUrl: './dialog.scss',
})
export class Dialog {
  readonly open = input.required<boolean>();
  readonly title = input<string>();
  readonly showClose = input(true);
  readonly size = input<'sm' | 'md'>('md');
  readonly closed = output<void>();

  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialogEl');

  constructor() {
    // Sync signal state to the native <dialog> DOM API.
    // Pattern: https://dev.to/sridhar_natuva_2b5e2beef0/we-deleted-our-focus-trap-scroll-lock-and-toggle-logic-the-browser-already-does-it-42a
    effect(() => {
      const el = this.dialog().nativeElement;
      const shouldBeOpen = this.open();

      if (shouldBeOpen && !el.open) {
        el.showModal();
      } else if (!shouldBeOpen && el.open) {
        el.close();
      }
    });
  }

  protected onCancel(event: Event): void {
    event.preventDefault();
    this.closed.emit();
  }

  protected onBackdropClick(event: MouseEvent): void {
    if (event.target === event.currentTarget) {
      this.closed.emit();
    }
  }
}
