import { Component } from '@angular/core';
import { RouterModule } from '@angular/router';
import { isDemoSource } from './flights/flights-source-mode';

@Component({
  imports: [RouterModule],
  selector: 'app-root',
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  readonly isDemo = isDemoSource();
}
