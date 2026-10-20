import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthStore } from '../../auth/services/auth-store';
import { ViewSwitcher } from '../view-switcher/view-switcher';

@Component({
  selector: 'bifrost-topbar',
  imports: [RouterLink, ViewSwitcher],
  templateUrl: './topbar.html',
  styleUrl: './topbar.scss',
})
export class Topbar {
  private readonly destroyRef = inject(DestroyRef);
  protected readonly authStore = inject(AuthStore);

  protected readonly homeLink = computed(() =>
    this.authStore.isTeacher() ? '/teacher' : '/',
  );

  protected readonly switcherRevealed = signal(false);

  private revealTimer: ReturnType<typeof setTimeout> | null = null;
  private hideTimer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    this.destroyRef.onDestroy(() => this.cancelTimers());
  }

  protected onBrandEnter(): void {
    if (this.switcherRevealed()) return;
    this.revealTimer = setTimeout(() => {
      this.switcherRevealed.set(true);
      this.scheduleHide();
    }, 1500);
  }

  protected onBrandLeave(): void {
    // Only cancel if the switcher hasn't been revealed yet.
    if (!this.switcherRevealed()) {
      this.cancelRevealTimer();
    }
  }

  protected onSwitcherEnter(): void {
    this.cancelHideTimer();
  }

  protected onSwitcherLeave(): void {
    this.scheduleHide();
  }

  private scheduleHide(): void {
    this.cancelHideTimer();
    this.hideTimer = setTimeout(() => this.switcherRevealed.set(false), 5000);
  }

  private cancelTimers(): void {
    this.cancelRevealTimer();
    this.cancelHideTimer();
  }

  private cancelRevealTimer(): void {
    if (this.revealTimer) {
      clearTimeout(this.revealTimer);
      this.revealTimer = null;
    }
  }

  private cancelHideTimer(): void {
    if (this.hideTimer) {
      clearTimeout(this.hideTimer);
      this.hideTimer = null;
    }
  }
}
