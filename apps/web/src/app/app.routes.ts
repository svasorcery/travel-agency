import type { Routes } from '@angular/router';
import { tripsCanDeactivate } from './trips/trips.guards';

export const appRoutes: Routes = [
  {
    path: 'trips',
    loadComponent: () => import('./trips/trips-page.component').then((m) => m.TripsPageComponent),
    canDeactivate: [tripsCanDeactivate],
  },
  {
    path: 'trips/:id',
    loadComponent: () => import('./trips/trips-page.component').then((m) => m.TripsPageComponent),
    canDeactivate: [tripsCanDeactivate],
  },
  {
    path: 'hotels/reservations/:id',
    loadComponent: () => import('./hotels/hotels-page.component').then((m) => m.HotelsPageComponent),
  },
  {
    path: 'hotels/reservations',
    loadComponent: () => import('./hotels/hotels-page.component').then((m) => m.HotelsPageComponent),
  },
  { path: 'hotels', loadComponent: () => import('./hotels/hotels-page.component').then((m) => m.HotelsPageComponent) },
  {
    path: 'support/operator/cases/:id',
    data: { supportMode: 'operator' },
    loadComponent: () => import('./support/support-page.component').then((m) => m.SupportPageComponent),
  },
  {
    path: 'support/operator',
    data: { supportMode: 'operator' },
    loadComponent: () => import('./support/support-page.component').then((m) => m.SupportPageComponent),
  },
  {
    path: 'support/cases/:id',
    data: { supportMode: 'customer' },
    loadComponent: () => import('./support/support-page.component').then((m) => m.SupportPageComponent),
  },
  {
    path: 'support',
    data: { supportMode: 'customer' },
    loadComponent: () => import('./support/support-page.component').then((m) => m.SupportPageComponent),
  },
  {
    path: 'flights/travelers',
    loadComponent: () => import('./flights/saved-travelers-page.component').then((m) => m.SavedTravelersPageComponent),
  },
  {
    path: 'flights/orders',
    loadComponent: () => import('./flights/flight-orders-page.component').then((m) => m.FlightOrdersPageComponent),
  },
  {
    path: 'flights/orders/:aggregateId',
    loadComponent: () => import('./flights/flight-order-page.component').then((m) => m.FlightOrderPageComponent),
  },
  {
    path: 'flights',
    loadComponent: () => import('./flights/flights-page.component').then((m) => m.FlightsPageComponent),
  },
  {
    path: 'status',
    loadComponent: () => import('./status/status-page.component').then((m) => m.StatusPageComponent),
  },
  { path: '', redirectTo: 'flights', pathMatch: 'full' },
];
