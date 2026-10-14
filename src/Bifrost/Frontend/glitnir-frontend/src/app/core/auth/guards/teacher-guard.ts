import { inject } from '@angular/core';
import { CanMatchFn } from '@angular/router';
import { AuthStore } from '../services/auth-store';

export const teacherGuard: CanMatchFn = async (_route, segments) => {
  const store = inject(AuthStore);
  const returnUrl = '/' + segments.map((segment) => segment.path).join('/');

  if (!store.isAuthenticated() || !store.isTeacher()) {
    await store.loadIdentity('teacher');
  }

  if (store.isTeacher()) {
    return true;
  }

  store.login(returnUrl);
  return false;
};
