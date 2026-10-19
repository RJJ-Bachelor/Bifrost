import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { HomeComponent } from './home.component';
import { TestPageComponent } from './test-page.component';

const routes: Routes = [
  { path: '', pathMatch: 'full', component: HomeComponent },
  { path: 'students', component: TestPageComponent, data: { role: 'students' } },
  { path: 'students/sessions/:sessionId', component: TestPageComponent, data: { role: 'students' } },
  { path: 'teachers', component: TestPageComponent, data: { role: 'teachers' } },
  { path: 'teachers/sessions/:sessionId', component: TestPageComponent, data: { role: 'teachers' } },
  { path: 'overview', pathMatch: 'full', redirectTo: '' },
  { path: 'students/requests', pathMatch: 'full', redirectTo: 'students' },
  { path: 'teachers/requests', pathMatch: 'full', redirectTo: 'teachers' },
  { path: 'requests', pathMatch: 'full', redirectTo: 'students' },
  { path: '**', redirectTo: '' }
];

@NgModule({ imports: [RouterModule.forRoot(routes, { scrollPositionRestoration: 'enabled' })], exports: [RouterModule] })
export class AppRoutingModule { }
