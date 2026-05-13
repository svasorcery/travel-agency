import type { Routes } from '@angular/router';

export const appRoutes: Routes = [
  {
    path: 'status',
    loadComponent: () => import('./status/status-page.component').then((m) => m.StatusPageComponent),
  },
  { path: '', redirectTo: 'status', pathMatch: 'full' },
];
