import { Component, computed, input, output, signal } from '@angular/core';
import { AuthorRole, HelpRequestMessage } from '../../help-request.model';

@Component({
  selector: 'bifrost-chat',
  templateUrl: './chat.html',
  styleUrl: './chat.scss',
})
export class Chat {
  readonly messages = input.required<HelpRequestMessage[]>();
  readonly viewerRole = input.required<AuthorRole>();
  readonly canSend = input.required<boolean>();
  readonly sending = input.required<boolean>();
  readonly error = input<string | null>(null);
  readonly closeLabel = input<string>('Luk');
  readonly sendLabel = input<string>('Send');
  readonly composerHint = input<string | null>(null);
  readonly placeholder = input<string>('Skriv en besked...');

  readonly sent = output<string>();
  readonly closed = output<void>();

  protected readonly draft = signal('');

  protected readonly visibleMessages = computed(() =>
    this.messages()
      .filter((m) => m.authorRole === 'student' || m.authorRole === 'teacher')
      .sort((a, b) => a.sequenceNumber - b.sequenceNumber),
  );

  protected isMine(message: HelpRequestMessage): boolean {
    return message.authorRole === this.viewerRole();
  }

  protected isTeacherMessage(message: HelpRequestMessage): boolean {
    return message.authorRole === 'teacher';
  }

  protected onSubmit(event: Event): void {
    event.preventDefault();
    this.submitDraft();
  }

  protected submitDraft(): void {
    const text = this.draft().trim();
    if (!text || this.sending()) return;
    this.sent.emit(text);
    this.draft.set('');
  }

  protected onClose(): void {
    this.closed.emit();
  }
}
