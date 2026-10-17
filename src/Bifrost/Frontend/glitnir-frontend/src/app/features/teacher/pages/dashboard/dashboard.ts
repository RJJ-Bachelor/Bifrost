import { Component } from '@angular/core';
import { Topbar } from '../../../../core/layout/topbar/topbar';

@Component({
  imports: [Topbar],
  selector: 'bifrost-dashboard',
  styleUrl: './dashboard.scss',
  templateUrl: './dashboard.html',
})
export class Dashboard {}
