import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, Input, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { finalize, timeout } from 'rxjs';
import { HeimdallService, IdentityTransferCode } from '../../heimdall.service';

@Component({
  selector: 'app-student-identity-transfer',
  imports: [CommonModule, FormsModule],
  templateUrl: './student-identity-transfer.component.html',
  styleUrl: './student-identity-transfer.component.css'
})
export class StudentIdentityTransferComponent {
  private readonly api = inject(HeimdallService);
  private readonly destroyRef = inject(DestroyRef);
  @Input({ required: true }) userId = '';
  generatedCode: IdentityTransferCode | null = null;
  redeemCode = '';
  busy = false;
  error = '';
  notice = '';

  get canRedeem() { return /^[A-HJ-NP-Z2-9]{8}$/.test(this.redeemCode.trim().toUpperCase()); }

  createCode() {
    if (this.busy) return;
    this.busy = true;
    this.error = '';
    this.notice = '';
    this.generatedCode = null;
    this.api.post<IdentityTransferCode>('students/identity-transfer-codes', null).pipe(
      timeout(30000), takeUntilDestroyed(this.destroyRef), finalize(() => { this.busy = false; })
    ).subscribe({
      next: code => { this.generatedCode = code; },
      error: failure => { this.handleError(failure); }
    });
  }

  redeemIdentity() {
    if (this.busy || !this.canRedeem) return;
    this.busy = true;
    this.error = '';
    this.notice = '';
    const code = this.redeemCode.trim().toUpperCase();
    this.api.post<void>('students/identity-transfer-codes/redeem', { code }).pipe(
      timeout(30000), takeUntilDestroyed(this.destroyRef), finalize(() => { this.busy = false; })
    ).subscribe({
      next: () => {
        // Conversation selections belong to the browser's previous student identity.
        try {
          for (let index = sessionStorage.length - 1; index >= 0; index--) {
            const key = sessionStorage.key(index);
            if (key?.startsWith('bifrost-students-')) sessionStorage.removeItem(key);
          }
        } catch { /* Identity transfer also works when browser storage is unavailable. */ }
        // Reload identity, session data and the notification connection with the new cookie.
        window.location.replace('/students');
      },
      error: failure => { this.handleError(failure); }
    });
  }

  async copyCode() {
    if (!this.generatedCode || this.busy) return;
    try {
      await navigator.clipboard.writeText(this.generatedCode.code);
      this.notice = 'Koden er kopieret.';
    } catch {
      this.notice = 'Markér og kopiér koden i feltet ovenfor.';
    }
  }

  private handleError(failure: unknown) {
    const status = failure instanceof HttpErrorResponse ? failure.status : 0;
    this.error = status === 404 ? 'Koden findes ikke, er udløbet eller er allerede brugt.'
      : status === 400 ? 'Indtast en gyldig overførselskode på 8 tegn.'
      : status === 429 ? 'For mange forsøg. Vent et minut, og prøv igen.'
      : status === 401 ? 'Din studenteridentitet kunne ikke bekræftes. Genindlæs siden, og prøv igen.'
      : 'Overførslen kunne ikke gennemføres. Kontrollér forbindelsen, og prøv igen.';
  }
}
