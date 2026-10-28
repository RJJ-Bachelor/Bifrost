import { Component, computed, inject, input, linkedSignal, output, signal } from '@angular/core';
import { Dialog } from '../../../../shared/components/dialog/dialog';
import { FaqItem } from '../../faq-item.model';
import { FaqApi } from '../../services/faq-api';

@Component({
  selector: 'bifrost-faq-delete-dialog',
  imports: [Dialog],
  templateUrl: './faq-delete-dialog.html',
  styleUrl: './faq-delete-dialog.scss',
})
export class FaqDeleteDialog {
  private readonly faqApi = inject(FaqApi);

  /** null = closed, FaqItem = open for the given item */
  readonly item = input<FaqItem | null>(null);

  readonly deleted = output<void>();
  readonly cancelled = output<void>();

  protected readonly dialogOpen = computed(() => this.item() !== null);
  protected readonly deleting = signal(false);

  // linkedSignal: error resets automatically when the target item changes.
  protected readonly error = linkedSignal<FaqItem | null, string | null>({
    source: () => this.item(),
    computation: () => null,
  });

  protected onCancel(): void {
    this.cancelled.emit();
  }

  protected onConfirm(): void {
    const target = this.item();
    if (target === null || this.deleting()) return;

    this.deleting.set(true);
    this.faqApi.deleteFaq(target.id).subscribe({
      next: () => {
        this.deleting.set(false);
        this.deleted.emit();
      },
      error: () => {
        this.deleting.set(false);
        this.error.set('Kunne ikke slette FAQ. Prøv igen.');
      },
    });
  }
}
