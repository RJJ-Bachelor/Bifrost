import { Component, computed, input, output } from '@angular/core';
import { ListRow } from '../../../../shared/components/list-row/list-row';
import { SessionGeneration } from '../../../session';

@Component({
  selector: 'bifrost-faq-archive-list',
  imports: [ListRow],
  templateUrl: './faq-archive-list.html',
  styleUrl: './faq-archive-list.scss',
})
export class FaqArchiveList {
  readonly items = input.required<SessionGeneration[]>();
  readonly maxItems = input<number>(3);

  readonly generationSelected = output<SessionGeneration>();
  readonly showAll = output<void>();

  protected readonly visible = computed(() =>
    this.items().slice(0, this.maxItems()),
  );

  protected readonly hasMore = computed(
    () => this.items().length > this.visible().length,
  );
}
