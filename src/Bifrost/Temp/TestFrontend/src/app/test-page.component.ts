import { Component, DestroyRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { Observable, finalize } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HeimdallService } from './heimdall.service';

@Component({
  selector: 'app-test-page',
  imports: [CommonModule, FormsModule],
  templateUrl: './test-page.component.html'
})
export class TestPageComponent {
  readonly api = inject(HeimdallService);
  private readonly destroyRef = inject(DestroyRef);
  readonly page = inject(ActivatedRoute).snapshot.data['page'] as string;
  busy = false;
  result: unknown;
  error = '';
  endpoint = '';
  id = 'test-' + Date.now();
  message = 'Hej fra Bifrost testfrontend';
  readonly titles: Record<string, string> = { overview: 'Systemstatus', students: 'Anonym elev', requests: 'Mine forespørgsler', teachers: 'Lærer' };
  constructor() { this.load(); }
  load() {
    const paths: Record<string, string> = { overview: 'monitoring/alive', students: 'students/me', requests: 'students/requests', teachers: 'teachers/me' };
    this.endpoint = 'GET /api/' + paths[this.page];
    this.execute(this.api.get(paths[this.page]));
  }
  send(create = false) {
    const path = create ? 'students/createrequest' : this.page + '/sendmessages';
    this.endpoint = 'POST /api/' + path;
    this.execute(this.api.post(path, { id: this.id.trim(), message: this.message.trim() }));
  }
  createSession() {
    this.endpoint = 'POST /api/teachers/createsession';
    this.execute(this.api.post('teachers/createsession', {}));
  }
  private execute(request: Observable<unknown>) {
    this.busy = true;
    this.error = '';
    this.result = undefined;
    request.pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.busy = false)).subscribe({
      next: result => this.result = result,
      error: (error: HttpErrorResponse) => {
        this.error = error.status === 401 ? 'Du skal logge ind som lærer. Hvis sessionen er udløbet, skal du logge ind igen.'
          : error.status === 403 ? 'Adgang afvist. Din konto skal have rollen Teacher.'
          : error.status === 0 || error.status === 502 || error.status === 503 || error.status === 504 ? 'Kan ikke kontakte systemet. Kontrollér at Heimdall og Eir kører.'
          : 'Forespørgslen fejlede (HTTP ' + error.status + ').';
        this.result = error.error || undefined;
      }
    });
  }
}
