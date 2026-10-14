import { Routes } from '@angular/router';
import { studentGuard } from './core/auth/guards/student-guard';
import { teacherGuard } from './core/auth/guards/teacher-guard';

export const routes: Routes = [
  {
    path: 'teacher',
    canMatch: [teacherGuard],
    loadChildren: () =>
      import('./features/teacher/teacher.routes').then((m) => m.teacherRoutes),
  },
  {
    path: '',
    canMatch: [studentGuard],
    loadChildren: () =>
      import('./features/student/student.routes').then((m) => m.studentRoutes),
  },
  {
    path: '**',
    loadComponent: () =>
      import('./features/not-found/not-found').then((m) => m.NotFound),
  },
];
