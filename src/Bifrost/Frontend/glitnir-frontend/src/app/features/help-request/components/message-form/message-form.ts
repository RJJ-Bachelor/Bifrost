import { Component, input, output, signal } from '@angular/core';
import { form, FormField, validate } from '@angular/forms/signals';

interface MessageFormModel {
  message: string;
}

@Component({
  imports: [FormField],
  selector: 'bifrost-message-form',
  styleUrl: './message-form.scss',
  templateUrl: './message-form.html',
})
export class MessageForm {
  readonly placeholder = input('Beskriv kort hvad du har brug for hjælp til, eller hvad du vil spørge om...');
  readonly submitLabel = input('Stil dig i kø');
  readonly submitting = input(false);
  readonly error = input<string | null>(null);

  readonly submitted = output<string>();

  protected readonly model = signal<MessageFormModel>({ message: '' });
  protected readonly messageForm = form(this.model, (path) => {
    validate(path.message, ({ value }) =>
      value().trim().length === 0
        ? { kind: 'required', message: 'Skriv venligst et spørgsmål' }
        : null,
    );
  });

  protected onSubmit(event: Event): void {
    event.preventDefault();
    if (this.messageForm().invalid()) return;

    this.submitted.emit(this.model().message.trim());
  }
}
