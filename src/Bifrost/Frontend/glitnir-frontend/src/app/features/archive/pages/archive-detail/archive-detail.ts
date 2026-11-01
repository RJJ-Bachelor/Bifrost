import { Component, computed, inject, input, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Topbar } from '../../../../core/layout/topbar/topbar';
import { Card } from '../../../../shared/components/card/card';
import { Dialog } from '../../../../shared/components/dialog/dialog';
import { FaqList, type FaqItem } from '../../../faq';
import {
  Chat,
  QuestionList,
  type HelpRequest,
  type HelpRequestMessage,
} from '../../../help-request';
import { ArchiveApi, type ArchiveViewerRole } from '../../services/archive-api';
import type { ArchivedConversation } from '../../archive.model';

function toQuestionRow(conversation: ArchivedConversation): HelpRequest {
  const question = conversation.messages.find((m) => m.messageType === 'studentQuestion');
  return {
    id: conversation.id,
    displayNumber: conversation.displayNumber,
    isFaq: false,
    status: conversation.status,
    message: question?.text ?? '',
    helpType: conversation.helpType,
    requestNumber: conversation.requestNumber,
    queuedAt: conversation.queuedAt,
    assignedTeacherName: null,
  };
}

@Component({
  selector: 'bifrost-archive-detail',
  imports: [Topbar, Card, Dialog, FaqList, QuestionList, Chat],
  templateUrl: './archive-detail.html',
  styleUrl: './archive-detail.scss',
})
export class ArchiveDetail {
  private readonly archiveApi = inject(ArchiveApi);
  private readonly router = inject(Router);

  readonly id = input.required<string>();
  readonly archiveId = input.required<string>();
  readonly viewerRole = input.required<ArchiveViewerRole>();

  protected readonly archive = this.archiveApi.getArchive(
    this.viewerRole,
    () => this.id(),
    () => this.archiveId(),
  );

  protected readonly title = computed(() => this.archive.value()?.title ?? 'Arkiv');
  protected readonly isTeacher = computed(() => this.viewerRole() === 'teacher');

  protected readonly faqItems = computed<FaqItem[]>(() =>
    (this.archive.value()?.faqs ?? []).map((faq) => ({
      id: faq.id,
      question: faq.question,
      answer: faq.answer ?? '(Ikke besvaret)',
    })),
  );

  private readonly conversations = computed(
    () => this.archive.value()?.conversations ?? [],
  );

  protected readonly questionRows = computed<HelpRequest[]>(() =>
    this.conversations().map(toQuestionRow),
  );

  protected readonly selectedConversation = signal<ArchivedConversation | null>(null);

  protected openConversation(row: HelpRequest): void {
    const conversation = this.conversations().find((c) => c.id === row.id);
    if (conversation) this.selectedConversation.set(conversation);
  }

  protected closeConversation(): void {
    this.selectedConversation.set(null);
  }

  protected toMessages(conversation: ArchivedConversation): HelpRequestMessage[] {
    return conversation.messages.map((message) => ({
      id: message.id,
      authorRole: message.authorRole,
      messageType: message.messageType,
      text: message.text,
      createdAt: message.createdAt,
      sequenceNumber: message.sequenceNumber,
    }));
  }

  protected back(): void {
    if (this.isTeacher()) {
      this.router.navigate(['/teacher/sessions', this.id()]);
    } else {
      this.router.navigate(['/sessions', this.id()]);
    }
  }
}
