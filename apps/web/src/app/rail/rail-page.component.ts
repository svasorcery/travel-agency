import { Component } from '@angular/core';
import { RailSearchComponent } from './rail-search.component';
@Component({
  selector: 'app-rail-page',
  standalone: true,
  imports: [RailSearchComponent],
  templateUrl: './rail-page.component.html',
  styleUrl: './rail-page.component.scss',
})
export class RailPageComponent {}
