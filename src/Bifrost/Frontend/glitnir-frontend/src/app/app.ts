import { Component, effect, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { AuthStore } from './core/auth/services/auth-store';
import { RealtimeService } from './core/realtime/realtime.service';

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
  private readonly realtime = inject(RealtimeService);

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

    effect(() => {
      const role = this.authStore.role();
      if (role === null) {
        void this.realtime.disconnect();
      } else {
        void this.realtime.connect(role);
      }
    });
  }
}
