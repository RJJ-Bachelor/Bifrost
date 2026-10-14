import { Routes } from '@angular/router';

export const teacherRoutes: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./pages/session-list/session-list').then((m) => m.SessionList),
  },
  {
    path: 'dashboard',
    loadComponent: () =>
      import('./pages/dashboard/dashboard').then((m) => m.Dashboard),
  },
  {
    path: 'sessions/:id',
    loadComponent: () =>
      import('./pages/session/session').then((m) => m.Session),
  },
  {
    path: 'sessions/:id/answer/:requestId',
    loadComponent: () =>
      import('./pages/answer-question/answer-question').then(
        (m) => m.AnswerQuestion,
      ),
  },
];
