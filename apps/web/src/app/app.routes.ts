import type { Routes } from '@angular/router';

export const appRoutes: Routes = [
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
