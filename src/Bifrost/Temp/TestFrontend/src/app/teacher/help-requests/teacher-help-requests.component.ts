import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';
import type { TestPageComponent } from '../../test-page.component';

@Component({
  selector: 'app-teacher-help-requests',
  imports: [CommonModule],
  templateUrl: './teacher-help-requests.component.html'
})
export class TeacherHelpRequestsComponent {
  @Input({ required: true }) view!: TestPageComponent;
}
