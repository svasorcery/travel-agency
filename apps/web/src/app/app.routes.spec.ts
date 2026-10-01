import { RenderMode } from '@angular/ssr';
import { appRoutes } from './app.routes';
import { serverRoutes } from './app.routes.server';

describe('Flights orders routing', () => {
  it('provides a client-rendered list before the single order route', () => {
    const list = appRoutes.findIndex((route) => route.path === 'flights/orders');
    expect(list).toBeGreaterThanOrEqual(0);
    expect(list).toBeLessThan(appRoutes.findIndex((route) => route.path === 'flights/orders/:aggregateId'));
    expect(serverRoutes.find((route) => route.path === 'flights/orders')?.renderMode).toBe(RenderMode.Client);
  });
});
