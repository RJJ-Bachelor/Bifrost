import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, input, linkedSignal, output, signal } from '@angular/core';
import { FormField, form, required, validate } from '@angular/forms/signals';
import { IdentityTransferCode } from '../../../../../../core/auth/auth.model';
import { AuthApi } from '../../../../../../core/auth/services/auth-api';
import { Dialog } from '../../../../../../shared/components/dialog/dialog';

type TransferStep = 'choose' | 'share' | 'import';

interface ImportFormModel {
  code: string;
}

@Component({
  selector: 'bifrost-identity-transfer-dialog',
  imports: [Dialog, FormField],
  templateUrl: './identity-transfer-dialog.html',
  styleUrl: './identity-transfer-dialog.scss',
})
export class IdentityTransferDialog {
  private readonly authApi = inject(AuthApi);

  readonly open = input.required<boolean>();
  readonly transferred = output<void>();
  readonly closed = output<void>();

  // linkedSignal: step and form reset automatically whenever the dialog opens.
  protected readonly step = linkedSignal<boolean, TransferStep>({
    source: () => this.open(),
    computation: () => 'choose',
  });

  protected readonly model = linkedSignal<boolean, ImportFormModel>({
    source: () => this.open(),
    computation: () => ({ code: '' }),
  });

  protected readonly importForm = form(this.model, (path) => {
    required(path.code);
    validate(path.code, ({ value }) =>
      value().trim().length === 8 ? null : { kind: 'length' },
    );
  });

  protected readonly transferCode = signal<IdentityTransferCode | null>(null);
  protected readonly copied = signal(false);

  protected readonly shareLoading = signal(false);
  protected readonly shareError = signal<string | null>(null);

  protected readonly importLoading = signal(false);
  protected readonly importError = signal<string | null>(null);

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

    await navigator.clipboard.writeText(code);
    this.copied.set(true);
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
