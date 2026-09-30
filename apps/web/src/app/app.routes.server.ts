import { RenderMode, type ServerRoute } from '@angular/ssr';

export const serverRoutes: ServerRoute[] = [
  {
    path: 'flights/orders/:aggregateId',
    renderMode: RenderMode.Client,
  },
  {
    path: 'flights',
    renderMode: RenderMode.Client,
  },
  {
    path: 'status',
    renderMode: RenderMode.Client,
  },
  {
    path: '**',
    renderMode: RenderMode.Prerender,
  },
];
