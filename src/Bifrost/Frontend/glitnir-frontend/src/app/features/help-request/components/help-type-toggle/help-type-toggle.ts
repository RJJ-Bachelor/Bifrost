import { Component, input, output } from '@angular/core';
import { HelpType } from '../../help-request.model';

@Component({
  selector: 'bifrost-help-type-toggle',
  imports: [],
  templateUrl: './help-type-toggle.html',
  styleUrl: './help-type-toggle.scss',
})
export class HelpTypeToggle {
  readonly value = input<HelpType>('physical');
  readonly valueChange = output<HelpType>();
}
