import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { TestPageComponent } from './test-page.component';

const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'overview' },
  ...['overview', 'students', 'requests', 'teachers'].map(page => ({ path: page, component: TestPageComponent, data: { page } })),
  { path: '**', redirectTo: 'overview' }
];

@NgModule({ imports: [RouterModule.forRoot(routes)], exports: [RouterModule] })
export class AppRoutingModule { }
