import { Service, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { Identity, UserRole } from '../auth.model';
import { AuthApi } from './auth-api';

@Service()
export class AuthStore {
  private readonly api = inject(AuthApi);
  private readonly _user = signal<Identity | null>(null);

  readonly user = this._user.asReadonly();
  readonly isAuthenticated = computed(() => this._user() !== null);
  readonly role = computed<UserRole | null>(() => this.resolveRole(this._user()));
  readonly isTeacher = computed(() => this.role() === 'teacher');
  readonly isStudent = computed(() => this.role() === 'student');

  async loadIdentity(role: UserRole): Promise<void> {
    try {
      const identity =
        role === 'teacher'
          ? await firstValueFrom(this.api.fetchTeacherIdentity())
          : await firstValueFrom(this.api.fetchStudentIdentity());
      this._user.set(identity);
    } catch {
      this._user.set(null);
    }
  }

  login(returnUrl: string = '/'): void {
    const absoluteUrl = window.location.origin + returnUrl;
    window.location.assign(
      `${environment.authBaseUrl}/account/login?returnUrl=${encodeURIComponent(absoluteUrl)}`
    );
  }

  logout(): void {
    this._user.set(null);
    const returnUrl = window.location.origin + '/';
    window.location.assign(
      `${environment.authBaseUrl}/account/logout?returnUrl=${encodeURIComponent(returnUrl)}`
    );
  }

  private resolveRole(identity: Identity | null): UserRole | null {
    if (!identity) return null;
    const roles = identity.roles.map((role) => role.toLowerCase());
    if (roles.includes('teacher')) return 'teacher';
    if (roles.includes('student')) return 'student';
    return null;
  }
}
