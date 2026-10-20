import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { AuthStore } from './core/auth/services/auth-store';

@Component({
  selector: 'bifrost-root',
  imports: [RouterOutlet],
  templateUrl: './app.html',
  styleUrl: './app.scss',
  host: {
    '[class.theme-teacher]': 'authStore.isTeacher()',
  },
})
export class App {
  protected readonly authStore = inject(AuthStore);
}
