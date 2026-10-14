import { inject } from '@angular/core';
import { CanMatchFn } from '@angular/router';
import { AuthStore } from '../services/auth-store';

export const studentGuard: CanMatchFn = async (_route, segments) => {
  if (segments[0]?.path === 'teacher') {
    return false;
  }

  const store = inject(AuthStore);

  if (!store.isAuthenticated()) {
    await store.loadIdentity('student');
  }

  return true;
};
