import { Component, input, output, signal } from '@angular/core';
import { form, FormField, required } from '@angular/forms/signals';

interface JoinFormModel {
  code: string;
}

@Component({
  selector: 'bifrost-session-join-form',
  imports: [FormField],
  templateUrl: './session-join-form.html',
  styleUrl: './session-join-form.scss',
})
export class SessionJoinForm {
  readonly error = input<string | null>(null);
  readonly joined = output<string>();

  protected readonly model = signal<JoinFormModel>({ code: '' });
  protected readonly joinForm = form(this.model, (schemaPath) => {
    required(schemaPath.code);
  });

  protected onSubmit(event: Event): void {
    event.preventDefault();
    if (this.joinForm().invalid()) return;

    this.joined.emit(this.model().code.trim());
    this.model.set({ code: '' });
  }
}
