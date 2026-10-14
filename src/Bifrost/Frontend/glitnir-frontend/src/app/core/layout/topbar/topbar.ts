import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthStore } from '../../auth/services/auth-store';

@Component({
  selector: 'app-topbar',
  imports: [RouterLink],
  templateUrl: './topbar.html',
  styleUrl: './topbar.scss',
})
export class Topbar {
  protected readonly authStore = inject(AuthStore);
  protected readonly homeLink = computed(() =>
    this.authStore.isTeacher() ? '/teacher' : '/',
  );
}
