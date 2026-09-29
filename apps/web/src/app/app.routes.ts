import type { Routes } from '@angular/router';

export const appRoutes: Routes = [
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
