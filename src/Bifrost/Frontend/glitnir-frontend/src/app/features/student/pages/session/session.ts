import { Component, computed, inject, input, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Topbar } from '../../../../core/layout/topbar/topbar';
import { Card } from '../../../../shared/components/card/card';
import { HelpTypeToggle } from '../../../help-request/components/help-type-toggle/help-type-toggle';
import { MessageForm } from '../../../help-request/components/message-form/message-form';
import { HelpType } from '../../../help-request/help-request.model';
import { HelpRequestApi } from '../../../help-request/services/help-request-api';
import { SessionApi } from '../../../session/services/session-api';

@Component({
  imports: [Topbar, Card, HelpTypeToggle, MessageForm],
  selector: 'bifrost-student-session',
  styleUrl: './session.scss',
  templateUrl: './session.html',
})
export class Session {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly sessionApi = inject(SessionApi);
  private readonly helpRequestApi = inject(HelpRequestApi);

  readonly id = input.required<string>();

  protected readonly session = this.sessionApi.getStudentSession(() => this.id());
  protected readonly sessionTitle = computed(() => this.session.value()?.title ?? '');
  protected readonly helpType = signal<HelpType>('physical');
  protected readonly submitting = signal(false);
  protected readonly submitError = signal<string | null>(null);

  protected switchSession(): void {
    this.router.navigateByUrl('/');
  }

  protected onQuestionSubmitted(message: string): void {
    if (this.submitting()) return;

    this.submitting.set(true);
    this.submitError.set(null);

    this.helpRequestApi
      .createHelpRequest(this.id(), { message, helpType: this.helpType() })
      .subscribe({
        next: () => {
          this.submitting.set(false);
          this.router.navigate(['queue'], { relativeTo: this.route });
        },
        error: () => {
          this.submitting.set(false);
          this.submitError.set('Kunne ikke sende spørgsmålet. Prøv igen.');
        },
      });
  }
}
