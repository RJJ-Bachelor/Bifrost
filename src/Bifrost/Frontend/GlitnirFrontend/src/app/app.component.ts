import { HeimdallService } from './heimdall.service';
import { Component, inject } from '@angular/core';

@Component({
  selector: 'app-root',
  templateUrl: './app.component.html',
  standalone: false,
  styleUrl: './app.component.css'
})
export class AppComponent {
  readonly api = inject(HeimdallService);
  title = 'GlitnirFrontend';
}
