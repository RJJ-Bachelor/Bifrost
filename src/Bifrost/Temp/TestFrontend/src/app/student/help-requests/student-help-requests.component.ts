import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';
import { FormsModule } from '@angular/forms';
import type { TestPageComponent } from '../../test-page.component';

@Component({
  selector: 'app-student-help-requests',
  imports: [CommonModule, FormsModule],
  templateUrl: './student-help-requests.component.html'
})
export class StudentHelpRequestsComponent {
  @Input({ required: true }) view!: TestPageComponent;
}
