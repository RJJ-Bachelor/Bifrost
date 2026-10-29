import { Component, input, output } from '@angular/core';
import { Chat, type HelpRequestMessage } from '../../../../../help-request';

@Component({
  imports: [Chat],
  selector: 'bifrost-answer-digital-chat',
  styleUrl: './answer-digital-chat.scss',
  templateUrl: './answer-digital-chat.html',
})
export class AnswerDigitalChat {
  readonly messages = input.required<HelpRequestMessage[]>();
  readonly busy = input.required<boolean>();
  readonly error = input<string | null>(null);

  readonly sent = output<string>();
  readonly cancelRequested = output<void>();
}
