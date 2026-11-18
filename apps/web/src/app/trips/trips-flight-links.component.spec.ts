import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TripsFlightLinksComponent } from './trips-flight-links.component';
import { TripsFlightLinksStateService } from './trips-flight-links-state.service';

const trip = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
  order = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
async function setup() {
  const state = {
    capability: signal('enabled'),
    saved: signal({ tripId: trip, linksRevision: '1', links: [{ orderId: order, linkedAt: '2026-11-17T00:00:00Z' }] }),
    cards: signal([{ orderId: order, state: 'unavailable' }]),
    clean: signal(true),
    canMutate: signal(true),
    picker: signal(false),
    busy: signal(null),
    message: signal(''),
    mutate: vi.fn(),
    loadCandidates: vi.fn(),
  };
  TestBed.configureTestingModule({
    imports: [TripsFlightLinksComponent],
    providers: [provideRouter([])],
  }).overrideComponent(TripsFlightLinksComponent, {
    set: { providers: [{ provide: TripsFlightLinksStateService, useValue: state }] },
  });
  const fixture = TestBed.createComponent(TripsFlightLinksComponent);
  await fixture.whenStable();
  const root = fixture.nativeElement as HTMLElement;
  const find = (name: string) => [...root.querySelectorAll('button')].find((b) => b.textContent.trim() === name)!;
  return { fixture, root, find, state };
}
it('renders an owner route with no trip or query and explicit keyboard controls outside a form', async () => {
  const { root, find } = await setup();
  expect(root.querySelector('a')?.getAttribute('href')).toBe('/flights/orders/' + order);
  expect(root.querySelector('form')).toBeNull();
  expect([...root.querySelectorAll('button')].every((b) => b.type === 'button')).toBe(true);
  expect(root.querySelector('[role="status"][aria-live="polite"]')).not.toBeNull();
  expect(find('Убрать из поездки').disabled).toBe(false);
});
it('requires a clean plan to attach/remove and stops opening the picker at four', async () => {
  const { fixture, find, state } = await setup();
  fixture.componentRef.setInput('draftDirty', true);
  await fixture.whenStable();
  expect(find('Выбрать авиазаказ').disabled).toBe(true);
  expect(find('Убрать из поездки').disabled).toBe(true);
  find('Убрать из поездки').click();
  expect(state.mutate).not.toHaveBeenCalled();
  fixture.componentRef.setInput('draftDirty', false);
  state.saved.update((saved) => ({ ...saved, links: Array.from({ length: 4 }, () => saved.links[0]) }));
  await fixture.whenStable();
  expect(find('Выбрать авиазаказ').disabled).toBe(true);
  expect(find('Убрать из поездки').disabled).toBe(false);
});
