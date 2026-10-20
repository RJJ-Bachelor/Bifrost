import { Component } from '@angular/core';
import { Topbar } from '../../core/layout/topbar/topbar';

@Component({
  imports: [Topbar],
  selector: 'bifrost-not-found',
  styleUrl: './not-found.scss',
  templateUrl: './not-found.html',
})
export class NotFound {}
