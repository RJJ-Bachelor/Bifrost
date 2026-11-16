import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { HomeComponent } from './home.component';

const loadTestPage = () => import('./test-page.component').then(page => page.TestPageComponent);

const routes: Routes = [
  { path: '', pathMatch: 'full', component: HomeComponent },
  { path: 'students', loadComponent: loadTestPage, data: { role: 'students' } },
  { path: 'students/sessions/:sessionId', loadComponent: loadTestPage, data: { role: 'students' } },
  { path: 'teachers', loadComponent: loadTestPage, data: { role: 'teachers' } },
  { path: 'teachers/sessions/:sessionId', loadComponent: loadTestPage, data: { role: 'teachers' } },
  { path: 'overview', pathMatch: 'full', redirectTo: '' },
  { path: 'students/requests', pathMatch: 'full', redirectTo: 'students' },
  { path: 'teachers/requests', pathMatch: 'full', redirectTo: 'teachers' },
  { path: 'requests', pathMatch: 'full', redirectTo: 'students' },
  { path: '**', redirectTo: '' }
];

@NgModule({ imports: [RouterModule.forRoot(routes, { scrollPositionRestoration: 'enabled' })], exports: [RouterModule] })
export class AppRoutingModule { }
