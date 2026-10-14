import { inject } from '@angular/core';
import { CanMatchFn } from '@angular/router';
import { AuthStore } from '../services/auth-store';

export const teacherGuard: CanMatchFn = async () => {
  const store = inject(AuthStore);
  const returnUrl = window.location.pathname + window.location.search;

  if (!store.isAuthenticated() || !store.isTeacher()) {
    await store.loadIdentity('teacher');
  }

  if (store.isTeacher()) {
    return true;
  }

  store.login(returnUrl);
  return false;
};
