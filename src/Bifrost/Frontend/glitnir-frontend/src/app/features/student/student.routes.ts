import { Routes } from '@angular/router';

export const studentRoutes: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./pages/session-list/session-list').then((m) => m.SessionList),
  },
  {
    path: 'sessions/:id',
    loadComponent: () =>
      import('./pages/session/session').then((m) => m.Session),
  },
  {
    path: 'sessions/:id/queue',
    loadComponent: () =>
      import('./pages/queue/queue').then((m) => m.Queue),
  },
];
