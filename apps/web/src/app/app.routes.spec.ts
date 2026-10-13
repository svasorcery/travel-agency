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
describe('Support private routes', () => {
  it('uses lazy routes with explicit session contexts and client rendering before wildcard', () => {
    for (const path of ['support', 'support/cases/:id', 'support/operator', 'support/operator/cases/:id']) {
      const route = appRoutes.find((route) => route.path === path);
      expect(route?.data?.['supportMode']).toBe(path.includes('operator') ? 'operator' : 'customer');
      expect(route?.loadComponent).toBeDefined();
    }
    const wildcard = serverRoutes.findIndex((route) => route.path === '**');
    for (const path of ['support', 'support/**']) {
      const index = serverRoutes.findIndex((route) => route.path === path);
      expect(index).toBeGreaterThanOrEqual(0);
      expect(index).toBeLessThan(wildcard);
      expect(serverRoutes[index].renderMode).toBe(RenderMode.Client);
    }
  });
});
