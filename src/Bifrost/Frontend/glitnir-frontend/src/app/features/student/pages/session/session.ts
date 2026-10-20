import { Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Topbar } from '../../../../core/layout/topbar/topbar';
import { Card } from '../../../../shared/components/card/card';

@Component({
  imports: [Topbar, Card],
  selector: 'bifrost-student-session',
  styleUrl: './session.scss',
  templateUrl: './session.html',
})
export class Session {
  private readonly router = inject(Router);

  protected readonly sessionTitle = signal('');

  protected switchSession(): void {
    this.router.navigateByUrl('/');
  }
}
