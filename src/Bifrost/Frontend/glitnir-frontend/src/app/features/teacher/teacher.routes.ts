import { Routes } from '@angular/router';

export const teacherRoutes: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./pages/teacher-sessions/teacher-sessions').then(
        (m) => m.TeacherSessions,
      ),
  },
  {
    path: 'dashboard',
    loadComponent: () =>
      import('./pages/dashboard/dashboard').then((m) => m.Dashboard),
  },
  {
    path: 'sessions/:id',
    loadComponent: () =>
      import('./pages/session/teacher-session').then((m) => m.TeacherSession),
  },
  {
    path: 'sessions/:id/answer/:requestId',
    loadComponent: () =>
      import('./pages/answer-question/answer-question').then(
        (m) => m.AnswerQuestion,
      ),
  },
];
