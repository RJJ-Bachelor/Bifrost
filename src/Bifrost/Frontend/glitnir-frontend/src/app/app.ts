import { Component, effect, inject } from '@angular/core';
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

  constructor() {
    effect(() => {
      const href = this.authStore.isTeacher() ? 'favicon-uv.svg' : 'favicon.svg';
      let link = document.querySelector<HTMLLinkElement>('link[rel="icon"]');
      if (!link) {
        link = document.createElement('link');
        link.rel = 'icon';
        link.type = 'image/svg+xml';
        document.head.appendChild(link);
      }
      link.href = href;
    });
  }
}
