import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { TestPageComponent } from './test-page.component';

const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'overview' },
  { path: 'students/requests', component: TestPageComponent, data: { page: 'studentRequests' } },
  { path: 'teachers/requests', component: TestPageComponent, data: { page: 'teacherRequests' } },
  ...['overview', 'students', 'teachers'].map(page => ({ path: page, component: TestPageComponent, data: { page } })),
  { path: 'requests', pathMatch: 'full', redirectTo: 'students/requests' },
  { path: '**', redirectTo: 'overview' }
];

@NgModule({ imports: [RouterModule.forRoot(routes)], exports: [RouterModule] })
export class AppRoutingModule { }
