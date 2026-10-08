import { CommonModule } from '@angular/common';
import { Component, DestroyRef, inject } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute } from '@angular/router';
import { finalize } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HeimdallService, Identity, IdentityKind } from './heimdall.service';

@Component({
  selector: 'app-identity-page',
  imports: [CommonModule],
  templateUrl: './identity-page.component.html'
})
export class IdentityPageComponent {
  private readonly api = inject(HeimdallService);
  private readonly destroyRef = inject(DestroyRef);
  readonly kind = inject(ActivatedRoute).snapshot.data['kind'] as IdentityKind;
  readonly student = this.kind === 'students';
  busy = false;
  status: number | null = null;
  identity: Identity | null = null;
  error = '';

  constructor() { this.load(); }

  load() {
    if (this.busy) return;
    this.busy = true;
    this.status = null;
    this.error = '';
    this.identity = null;
    this.api.identity(this.kind).pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.busy = false)
    ).subscribe({
      next: response => {
        this.status = response.status;
        this.identity = response.body;
      },
      error: (error: HttpErrorResponse) => {
        this.status = error.status;
        this.error = error.status === 401 ? 'Adgang kræver login. Brug Login øverst til højre for at logge ind som lærer.'
          : error.status === 403 ? 'Adgang afvist. Kontoen skal have rollen Teacher.'
          : error.status === 0 || error.status >= 500 ? 'Kan ikke hente data. Kontrollér, at Heimdall og Eir kører.'
          : 'Forespørgslen blev afvist af serveren.';
      }
    });
  }
}
