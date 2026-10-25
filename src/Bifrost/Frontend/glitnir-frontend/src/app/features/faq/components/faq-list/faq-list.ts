import { Component, input, output, signal } from '@angular/core';
import { FaqItem } from '../../faq-item.model';

@Component({
  imports: [],
  selector: 'bifrost-faq-list',
  styleUrl: './faq-list.scss',
  templateUrl: './faq-list.html',
})
export class FaqList {
  readonly items = input.required<FaqItem[]>();
  readonly editable = input(false);

  readonly faqEditRequested = output<FaqItem>();
  readonly faqDeleteRequested = output<FaqItem>();

  protected readonly openIds = signal<Set<string>>(new Set());

  protected isOpen(id: string): boolean {
    return this.openIds().has(id);
  }

  protected toggle(id: string): void {
    const next = new Set(this.openIds());
    if (next.has(id)) next.delete(id);
    else next.add(id);
    this.openIds.set(next);
  }

  protected onEdit(event: Event, item: FaqItem): void {
    event.stopPropagation();
    this.faqEditRequested.emit(item);
  }

  protected onDelete(event: Event, item: FaqItem): void {
    event.stopPropagation();
    this.faqDeleteRequested.emit(item);
  }
}
