import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { filter, map } from 'rxjs';

@Component({
  selector: 'app-view-switcher',
  imports: [RouterLink],
  templateUrl: './view-switcher.html',
  styleUrl: './view-switcher.scss',
})
export class ViewSwitcher {
  private readonly router = inject(Router);

  private readonly url = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map(() => this.router.url),
    ),
    { initialValue: this.router.url },
  );

  protected readonly onTeacherSide = computed(() =>
    this.url().startsWith('/teacher'),
  );

  protected readonly target = computed(() =>
    this.onTeacherSide() ? '/' : '/teacher',
  );

  protected readonly label = computed(() =>
    this.onTeacherSide() ? 'Skift til student' : 'Skift til underviser',
  );
}
