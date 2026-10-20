import { Component, inject } from '@angular/core';
import { RouterModule } from '@angular/router';
import { isDemoSource } from './flights/flights-source-mode';
import { TripsAvailabilityService } from './trips/trips-availability.service';

@Component({
  imports: [RouterModule],
  selector: 'app-root',
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  readonly isDemo = isDemoSource();
  readonly trips = inject(TripsAvailabilityService);

  constructor() {
    if (!this.isDemo) this.trips.initialize(null);
  }
}
