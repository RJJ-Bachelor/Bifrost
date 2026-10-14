import { inject } from '@angular/core';
import { CanActivateFn } from '@angular/router';
import { AuthStore } from '../services/auth-store';

export const studentGuard: CanActivateFn = async () => {
  const store = inject(AuthStore);

  if (!store.isAuthenticated()) {
    await store.loadIdentity('student');
  }

  return true;
};
