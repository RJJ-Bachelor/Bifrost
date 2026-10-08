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
  title = 'TestFrontend';
}
