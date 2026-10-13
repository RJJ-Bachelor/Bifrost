import { Component, inject } from '@angular/core';
import { HeimdallService } from './heimdall.service';

@Component({
  selector: 'app-root',
  templateUrl: './app.component.html',
  standalone: false,
  styleUrl: './app.component.css'
})
export class AppComponent {
  readonly api = inject(HeimdallService);
  copyNotice = '';

  async copySessionLink() {
    if (!this.api.sessionLink) return;
    try {
      await navigator.clipboard.writeText(this.api.sessionLink);
      this.copyNotice = 'Sessionslinket er kopieret.';
    } catch {
      this.copyNotice = 'Markér og kopiér linket i feltet.';
    }
  }
}
