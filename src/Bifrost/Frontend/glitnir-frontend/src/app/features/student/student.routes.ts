import { Routes } from '@angular/router';

export const studentRoutes: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./pages/student-sessions/student-sessions').then(
        (m) => m.StudentSessions,
      ),
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
