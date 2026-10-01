import { DOCUMENT } from '@angular/common';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { NavigationStart, Router } from '@angular/router';
import type { FlightOrderResponse } from '@travel/api-client';
import { Subject } from 'rxjs';
import { FlightOrdersFeedService, type FlightOrdersFeedSnapshot } from './flight-orders-feed.service';
import { FlightsAuthService, type FlightsAuthStatus } from './flights-auth.service';

const owner = '88b83d41-0194-2098-c1f6-fe7351d41cf2';
const otherOwner = '5e486e1f-12f5-4f3f-a85f-875fa5bff428';
const orderId = '63fbba4d-4da6-41e7-9629-cb77d0ea6ad8';
const detailPath = `/flights/orders/${orderId}`;

function snapshot(): FlightOrdersFeedSnapshot {
  const item: FlightOrderResponse = {
    aggregateId: orderId,
    status: 'Held',
    totalAmount: 120,
    currency: 'EUR',
    itinerary: {
      slices: [
        {
          origin: 'LHR',
          destination: 'CDG',
          duration: 'PT1H',
          segments: [
            {
              origin: 'LHR',
              destination: 'CDG',
              departAt: '2030-06-01T10:00:00Z',
              arriveAt: '2030-06-01T11:00:00Z',
              carrierCode: 'XX',
              flightNumber: '100',
              cabinClass: 'economy',
            },
          ],
        },
      ],
      totalDuration: 'PT1H',
      isRoundTrip: false,
    },
    ticketNumbers: [],
    bookedAt: '2030-06-01T09:00:00Z',
    ticketedAt: null,
    cancelledAt: null,
    refundedAt: null,
  };
  return {
    items: [item],
    nextOffset: 20,
    hasMore: true,
    anchorId: orderId,
    anchorTop: 80,
    scrollY: 600,
    focusId: orderId,
  };
}

describe('FlightOrdersFeedService', () => {
  let auth: { status: ReturnType<typeof signal<FlightsAuthStatus>> };
  let events: Subject<NavigationStart>;
  let router: { url: string; events: Subject<NavigationStart> };
  let nativeHistory: { scrollRestoration: ScrollRestoration };

  beforeEach(() => {
    auth = { status: signal<FlightsAuthStatus>({ kind: 'authenticated', userId: owner }) };
    events = new Subject<NavigationStart>();
    router = { url: '/flights/orders', events };
    nativeHistory = { scrollRestoration: 'auto' };
  });

  afterEach(() => TestBed.resetTestingModule());

  function createService(path = '/flights/orders', withRouter = true, documentPath = path): FlightOrdersFeedService {
    router.url = path;
    TestBed.configureTestingModule({
      providers: [
        { provide: FlightsAuthService, useValue: auth },
        {
          provide: DOCUMENT,
          useValue: { defaultView: { history: nativeHistory, location: { pathname: documentPath } } },
        },
        ...(withRouter ? [{ provide: Router, useValue: router }] : []),
      ],
    });
    return TestBed.inject(FlightOrdersFeedService);
  }

  function navigate(target: string): void {
    events.next(new NavigationStart(1, target));
    router.url = target;
  }

  it('restores the last accumulated feed and its anchor and focus for the current owner', () => {
    const feed = createService();
    feed.save(owner, snapshot());
    expect(feed.restore(owner)).toEqual({
      items: [expect.objectContaining({ aggregateId: orderId })],
      nextOffset: 20,
      hasMore: true,
      anchorId: orderId,
      anchorTop: 80,
      scrollY: 600,
      focusId: orderId,
    });
  });

  it('keeps only the most recently saved feed', () => {
    const feed = createService();
    feed.save(owner, snapshot());
    feed.save(owner, { ...snapshot(), items: [], nextOffset: 40, hasMore: false });
    expect(feed.restore(owner)?.items).toEqual([]);
    expect(feed.restore(owner)?.nextOffset).toBe(40);
  });

  it('never returns a snapshot to another owner', () => {
    const feed = createService();
    feed.save(owner, snapshot());
    expect(feed.restore(otherOwner)).toBeNull();
    expect(feed.restore(owner)?.items).toHaveLength(1);
  });

  it('ignores a save that does not belong to the authenticated owner', () => {
    const feed = createService();
    feed.save(owner, snapshot());
    feed.save(otherOwner, { ...snapshot(), items: [] });
    expect(feed.restore(owner)?.items).toHaveLength(1);
    expect(feed.restore(otherOwner)).toBeNull();
  });

  it('matches the authenticated owner case-insensitively', () => {
    const feed = createService();
    feed.save(owner.toUpperCase(), snapshot());
    auth.status.set({ kind: 'authenticated', userId: owner.toUpperCase() });
    TestBed.tick();
    expect(feed.restore(owner)?.items).toHaveLength(1);
  });

  it('clears the cache immediately when restore observes a changed identity before the effect runs', () => {
    const feed = createService();
    feed.save(owner, snapshot());
    auth.status.set({ kind: 'authenticated', userId: otherOwner });
    expect(feed.restore(owner)).toBeNull();
    auth.status.set({ kind: 'authenticated', userId: owner });
    expect(feed.restore(owner)).toBeNull();
  });

  it('rejects a late save from the previous owner before the effect runs', () => {
    const feed = createService();
    feed.save(owner, snapshot());
    auth.status.set({ kind: 'authenticated', userId: otherOwner });
    feed.save(owner, snapshot());
    auth.status.set({ kind: 'authenticated', userId: owner });
    expect(feed.restore(owner)).toBeNull();
  });

  it('clears the cache when an identity change is observed by the auth effect', () => {
    const feed = createService();
    feed.save(owner, snapshot());
    TestBed.tick();
    auth.status.set({ kind: 'authenticated', userId: otherOwner });
    TestBed.tick();
    auth.status.set({ kind: 'authenticated', userId: owner });
    expect(feed.restore(owner)).toBeNull();
  });

  it.each<FlightsAuthStatus>([
    { kind: 'anonymous' },
    { kind: 'redirecting' },
    { kind: 'unavailable', message: 'fixture unavailable' },
    { kind: 'error', message: 'fixture expired' },
  ])('clears the cache when auth becomes $kind', (status) => {
    const feed = createService();
    feed.save(owner, snapshot());
    TestBed.tick();
    auth.status.set(status);
    TestBed.tick();
    auth.status.set({ kind: 'authenticated', userId: owner });
    expect(feed.restore(owner)).toBeNull();
  });

  it('rejects save and restore synchronously after logout', () => {
    const feed = createService();
    feed.save(owner, snapshot());
    auth.status.set({ kind: 'anonymous' });
    expect(feed.restore(owner)).toBeNull();
    feed.save(owner, snapshot());
    auth.status.set({ kind: 'authenticated', userId: owner });
    expect(feed.restore(owner)).toBeNull();
  });

  it('protects the cached DTO and nested arrays from mutation of the saved snapshot', () => {
    const feed = createService();
    const original = snapshot();
    feed.save(owner, original);
    original.items[0].ticketNumbers.push('fixture-ticket');
    original.items[0].itinerary.slices[0].segments[0].origin = 'JFK';
    original.items.push({ ...original.items[0], aggregateId: otherOwner });
    original.anchorTop = 999;
    const restored = feed.restore(owner);
    expect(restored?.items).toHaveLength(1);
    expect(restored?.items[0].ticketNumbers).toEqual([]);
    expect(restored?.items[0].itinerary.slices[0].segments[0].origin).toBe('LHR');
    expect(restored?.anchorTop).toBe(80);
  });

  it('protects the cache from mutation of a restored snapshot', () => {
    const feed = createService();
    feed.save(owner, snapshot());
    const restored = feed.restore(owner);
    if (restored === null) throw new Error('Fixture snapshot was not restored.');
    restored.items[0].itinerary.slices.splice(0);
    restored.items.splice(0);
    expect(feed.restore(owner)?.items[0].itinerary.slices).toHaveLength(1);
  });

  it('clears both the feed and the return marker', () => {
    const feed = createService(detailPath);
    feed.save(owner, snapshot());
    navigate('/flights/orders');
    feed.clear();
    expect(feed.restore(owner)).toBeNull();
    expect(feed.returningFromOrder()).toBe(false);
  });

  it('marks a return to the list only from a valid order detail when a cache exists', () => {
    const feed = createService(detailPath);
    feed.save(owner, snapshot());
    navigate('/flights/orders');
    expect(feed.returningFromOrder()).toBe(true);
    navigate('/flights');
    expect(feed.returningFromOrder()).toBe(false);
  });

  it('does not mark a return from detail without a saved feed', () => {
    const feed = createService(detailPath);
    navigate('/flights/orders');
    expect(feed.returningFromOrder()).toBe(false);
  });

  it.each(['/flights', '/status', '/flights/orders', '/flights/orders/invalid', `${detailPath}/extra`])(
    'does not mark a visit from %s as a detail return',
    (source) => {
      const feed = createService(source);
      feed.save(owner, snapshot());
      navigate('/flights/orders');
      expect(feed.returningFromOrder()).toBe(false);
    },
  );

  it.each(['/flights/orders?offset=20', '/flights/orders#fixture', detailPath])(
    'does not mark navigation to %s as returning to the list',
    (target) => {
      const feed = createService(detailPath);
      feed.save(owner, snapshot());
      navigate(target);
      expect(feed.returningFromOrder()).toBe(false);
    },
  );

  it('rejects the return marker synchronously if the owner changed', () => {
    const feed = createService(detailPath);
    feed.save(owner, snapshot());
    navigate('/flights/orders');
    auth.status.set({ kind: 'authenticated', userId: otherOwner });
    expect(feed.returningFromOrder()).toBe(false);
    auth.status.set({ kind: 'authenticated', userId: owner });
    expect(feed.restore(owner)).toBeNull();
  });

  it.each(['/flights/orders', detailPath])('uses manual native restoration on initial %s', (path) => {
    createService(path);
    expect(nativeHistory.scrollRestoration).toBe('manual');
  });

  it('restores the original native setting outside the list and detail corridor', () => {
    createService('/status');
    expect(nativeHistory.scrollRestoration).toBe('auto');
    navigate('/flights/orders');
    expect(nativeHistory.scrollRestoration).toBe('manual');
    navigate(detailPath);
    expect(nativeHistory.scrollRestoration).toBe('manual');
    navigate('/flights/orders/invalid');
    expect(nativeHistory.scrollRestoration).toBe('auto');
  });

  it('restores the original native setting and unsubscribes when the service is destroyed', () => {
    createService();
    TestBed.resetTestingModule();
    expect(nativeHistory.scrollRestoration).toBe('auto');
    navigate(detailPath);
    expect(nativeHistory.scrollRestoration).toBe('auto');
  });

  it('preserves an original manual setting when leaving the corridor', () => {
    nativeHistory.scrollRestoration = 'manual';
    createService();
    navigate('/status');
    expect(nativeHistory.scrollRestoration).toBe('manual');
    TestBed.resetTestingModule();
    expect(nativeHistory.scrollRestoration).toBe('manual');
  });

  it('uses the document corridor when the Router still has its initial URL', () => {
    createService('/', true, '/flights/orders');
    expect(nativeHistory.scrollRestoration).toBe('manual');
  });
  it('works without a Router provider and still uses the current document corridor', () => {
    const feed = createService('/flights/orders', false);
    feed.save(owner, snapshot());
    expect(feed.restore(owner)?.items).toHaveLength(1);
    expect(feed.returningFromOrder()).toBe(false);
    expect(nativeHistory.scrollRestoration).toBe('manual');
  });

  it('works for a server document with no browser history', () => {
    TestBed.configureTestingModule({
      providers: [
        { provide: FlightsAuthService, useValue: auth },
        { provide: DOCUMENT, useValue: { defaultView: null } },
      ],
    });
    const feed = TestBed.inject(FlightOrdersFeedService);
    feed.save(owner, snapshot());
    expect(feed.restore(owner)?.items).toHaveLength(1);
    expect(feed.returningFromOrder()).toBe(false);
  });
});
