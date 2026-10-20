import { Component, input, output, signal } from '@angular/core';
import { form, FormField, required } from '@angular/forms/signals';

interface CreateFormModel {
  title: string;
}

@Component({
  selector: 'bifrost-session-create-form',
  imports: [FormField],
  templateUrl: './session-create-form.html',
  styleUrl: './session-create-form.scss',
})
export class SessionCreateForm {
  readonly error = input<string | null>(null);
  readonly showCancel = input(false);
  readonly created = output<string>();
  readonly cancelled = output<void>();

  protected readonly model = signal<CreateFormModel>({ title: '' });
  protected readonly createForm = form(this.model, (schemaPath) => {
    required(schemaPath.title);
  });

  protected onSubmit(event: Event): void {
    event.preventDefault();
    if (this.createForm().invalid()) return;

    this.created.emit(this.model().title);
    this.model.set({ title: '' });
  }
}
