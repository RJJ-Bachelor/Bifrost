import { Component, input, signal } from '@angular/core';
import { FaqItem } from '../../faq-item.model';

@Component({
  imports: [],
  selector: 'bifrost-faq-list',
  styleUrl: './faq-list.scss',
  templateUrl: './faq-list.html',
})
export class FaqList {
  readonly items = input.required<FaqItem[]>();

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
}
