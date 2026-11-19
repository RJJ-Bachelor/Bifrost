import {HttpErrorResponse} from '@angular/common/http';
import {Component, inject, input, linkedSignal, output, signal} from '@angular/core';
import {FormField, form, required, validate} from '@angular/forms/signals';
import {IdentityTransferCode} from '../../../../core/auth/auth.model';
import {AuthApi} from '../../../../core/auth/services/auth-api';
import {PushNotificationService} from '../../../../core/pwa/services/push-notification.service';
import {Dialog} from '../../../../shared/components/dialog/dialog';

type TransferStep = 'choose' | 'share' | 'import';

interface ImportFormModel {
  code: string;
}

@Component({
  selector: 'bifrost-settings-dialog',
  imports: [Dialog, FormField],
  templateUrl: './settings-dialog.html',
  styleUrl: './settings-dialog.scss',
})
export class SettingsDialog {
  private readonly authApi = inject(AuthApi);
  protected readonly push = inject(PushNotificationService);

  readonly open = input.required<boolean>();
  readonly showTransfer = input(false);
  readonly transferred = output<void>();
  readonly closed = output<void>();

  protected readonly step = linkedSignal<boolean, TransferStep>({
    source: () => this.open(),
    computation: () => 'choose',
  });

  protected readonly model = linkedSignal<boolean, ImportFormModel>({
    source: () => this.open(),
    computation: () => ({code: ''}),
  });

  protected readonly importForm = form(this.model, (path) => {
    required(path.code);
    validate(path.code, ({value}) =>
      value().trim().length > 0 ? null : {kind: 'required'},
    );
  });

  protected readonly transferCode = linkedSignal<boolean, IdentityTransferCode | null>({
    source: () => this.open(),
    computation: () => null,
  });
  protected readonly copied = linkedSignal<boolean, boolean>({
    source: () => this.open(),
    computation: () => false,
  });

  protected readonly shareLoading = signal(false);
  protected readonly shareError = linkedSignal<boolean, string | null>({
    source: () => this.open(),
    computation: () => null,
  });

  protected readonly importLoading = signal(false);
  protected readonly importError = linkedSignal<boolean, string | null>({
    source: () => this.open(),
    computation: () => null,
  });

  protected async toggleNotifications(): Promise<void> {
    if (this.push.isSubscribed()) {
      await this.push.disable();
    } else {
      await this.push.enable();
    }
  }

  protected share(): void {
    this.step.set('share');
    this.generateCode();
  }

  protected startImport(): void {
    this.step.set('import');
  }

  protected goBack(): void {
    this.step.set('choose');
    this.shareError.set(null);
    this.importError.set(null);
  }

  protected close(): void {
    this.push.error.set(null);
    this.closed.emit();
  }

  protected generateCode(): void {
    if (this.shareLoading()) return;

    this.shareLoading.set(true);
    this.shareError.set(null);
    this.transferCode.set(null);
    this.copied.set(false);

    this.authApi.createIdentityTransferCode().subscribe({
      next: (result) => {
        this.shareLoading.set(false);
        this.transferCode.set(result);
      },
      error: () => {
        this.shareLoading.set(false);
        this.shareError.set('Kunne ikke oprette en overførselskode. Prøv igen.');
      },
    });
  }

  protected async copyCode(): Promise<void> {
    const code = this.transferCode()?.code;
    if (!code) return;

    try {
      await navigator.clipboard.writeText(code);
      this.copied.set(true);
    } catch {
      // Clipboard access may be blocked in restricted contexts.
    }
  }

  protected redeem(event: Event): void {
    event.preventDefault();
    if (this.importForm().invalid() || this.importLoading()) return;

    const code = this.model().code.trim().toUpperCase();

    this.importLoading.set(true);
    this.importError.set(null);

    this.authApi.redeemIdentityTransferCode(code).subscribe({
      next: () => {
        this.importLoading.set(false);
        this.transferred.emit();
      },
      error: (err: unknown) => {
        this.importLoading.set(false);
        this.importError.set(
          err instanceof HttpErrorResponse && err.status === 429
            ? 'For mange forsøg. Vent et øjeblik og prøv igen.'
            : 'Koden er ugyldig eller udløbet. Prøv igen.',
        );
      },
    });
  }
}
