import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FlightOrderHandoffService } from './flight-order-handoff.service';
import { FlightsAuthService, type FlightsAuthStatus } from './flights-auth.service';

const owner = '88b83d41-0194-2098-c1f6-fe7351d41cf2';
const otherOwner = '5e486e1f-12f5-4f3f-a85f-875fa5bff428';
const orderId = '63fbba4d-4da6-41e7-9629-cb77d0ea6ad8';
const recordedAt = Date.UTC(2030, 5, 1, 10);

describe('FlightOrderHandoffService', () => {
  let auth: { status: ReturnType<typeof signal<FlightsAuthStatus>> };
  let handoff: FlightOrderHandoffService;

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(recordedAt);
    auth = { status: signal<FlightsAuthStatus>({ kind: 'authenticated', userId: owner }) };
    TestBed.configureTestingModule({ providers: [{ provide: FlightsAuthService, useValue: auth }] });
    handoff = TestBed.inject(FlightOrderHandoffService);
  });

  afterEach(() => {
    TestBed.resetTestingModule();
    vi.useRealTimers();
  });

  it('preserves the recent hold and its deadline after B3 consumes the Held outcome once', () => {
    handoff.rememberHeld(orderId, owner);
    expect(handoff.takeOutcome(orderId, owner)).toBe('Held');
    expect(handoff.takeOutcome(orderId, owner)).toBeNull();
    expect(handoff.recentHeld(owner)).toBe(orderId);
    expect(handoff.recentHeldDeadline(owner)).toBe(recordedAt + 30_000);
  });

  it('keeps the deadline fixed at hold recording time across repeated reads', () => {
    handoff.rememberHeld(orderId, owner);
    vi.setSystemTime(recordedAt + 12_000);
    expect(handoff.recentHeld(owner)).toBe(orderId);
    expect(handoff.recentHeldDeadline(owner)).toBe(recordedAt + 30_000);
    vi.setSystemTime(recordedAt + 29_999);
    expect(handoff.recentHeldDeadline(owner)).toBe(recordedAt + 30_000);
  });

  it('clears the hold at exactly 30 seconds without removing the B3 outcome', () => {
    handoff.rememberHeld(orderId, owner);
    vi.setSystemTime(recordedAt + 30_000);
    expect(handoff.recentHeld(owner)).toBeNull();
    expect(handoff.recentHeldDeadline(owner)).toBeNull();
    vi.setSystemTime(recordedAt + 1_000);
    expect(handoff.recentHeld(owner)).toBeNull();
    expect(handoff.takeOutcome(orderId, owner)).toBe('Held');
  });

  it('expires the hold when the deadline is read first', () => {
    handoff.rememberHeld(orderId, owner);
    vi.setSystemTime(recordedAt + 30_000);
    expect(handoff.recentHeldDeadline(owner)).toBeNull();
    vi.setSystemTime(recordedAt + 1_000);
    expect(handoff.recentHeld(owner)).toBeNull();
  });

  it.each<FlightsAuthStatus>([
    { kind: 'anonymous' },
    { kind: 'redirecting' },
    { kind: 'unavailable', message: 'fixture unavailable' },
    { kind: 'error', message: 'fixture expired' },
  ])('clears hold and outcome while no list is mounted when auth becomes $kind', (status) => {
    handoff.rememberHeld(orderId, owner);
    TestBed.tick();
    auth.status.set(status);
    TestBed.tick();
    auth.status.set({ kind: 'authenticated', userId: owner });
    expect(handoff.recentHeld(owner)).toBeNull();
    expect(handoff.recentHeldDeadline(owner)).toBeNull();
    expect(handoff.takeOutcome(orderId, owner)).toBeNull();
  });

  it('clears hold and outcome across an observed A to B to A identity change without a list component', () => {
    handoff.rememberHeld(orderId, owner);
    TestBed.tick();
    auth.status.set({ kind: 'authenticated', userId: otherOwner });
    TestBed.tick();
    auth.status.set({ kind: 'authenticated', userId: owner });
    expect(handoff.recentHeld(owner)).toBeNull();
    expect(handoff.takeOutcome(orderId, owner)).toBeNull();
  });

  it('rejects a cross-owner read and clears old state synchronously before the effect runs', () => {
    handoff.rememberHeld(orderId, owner);
    auth.status.set({ kind: 'authenticated', userId: otherOwner });
    expect(handoff.recentHeld(owner)).toBeNull();
    auth.status.set({ kind: 'authenticated', userId: owner });
    expect(handoff.recentHeld(owner)).toBeNull();
    expect(handoff.takeOutcome(orderId, owner)).toBeNull();
  });

  it('checks current auth synchronously when the deadline is read', () => {
    handoff.rememberHeld(orderId, owner);
    auth.status.set({ kind: 'anonymous' });
    expect(handoff.recentHeldDeadline(owner)).toBeNull();
    auth.status.set({ kind: 'authenticated', userId: owner });
    expect(handoff.recentHeld(owner)).toBeNull();
    expect(handoff.takeOutcome(orderId, owner)).toBeNull();
  });

  it('does not reveal the current owner hint to another caller', () => {
    handoff.rememberHeld(orderId, owner);
    expect(handoff.recentHeld(otherOwner)).toBeNull();
    expect(handoff.recentHeldDeadline(otherOwner)).toBeNull();
    expect(handoff.recentHeld(owner)).toBe(orderId);
  });

  it('keeps hold and outcome through case-only owner changes', () => {
    handoff.rememberHeld(orderId, owner.toUpperCase());
    TestBed.tick();
    auth.status.set({ kind: 'authenticated', userId: owner.toUpperCase() });
    TestBed.tick();
    expect(handoff.recentHeld(owner)).toBe(orderId);
    expect(handoff.recentHeldDeadline(owner)).toBe(recordedAt + 30_000);
    expect(handoff.takeOutcome(orderId, owner)).toBe('Held');
  });

  it('keeps the Held deadline while replacing the B3 outcome with Confirmed', () => {
    handoff.rememberHeld(orderId, owner);
    vi.setSystemTime(recordedAt + 8_000);
    handoff.rememberConfirmed(orderId, owner);
    expect(handoff.takeOutcome(orderId, owner)).toBe('Confirmed');
    expect(handoff.takeOutcome(orderId, owner)).toBeNull();
    expect(handoff.recentHeld(owner)).toBe(orderId);
    expect(handoff.recentHeldDeadline(owner)).toBe(recordedAt + 30_000);
  });

  it('consumes a mismatched B3 outcome without destroying a valid recent hold', () => {
    handoff.rememberHeld(orderId, owner);
    expect(handoff.takeOutcome('different-order', owner)).toBeNull();
    expect(handoff.takeOutcome(orderId, owner)).toBeNull();
    expect(handoff.recentHeld(owner)).toBe(orderId);
  });

  it('rejects late Held writes from an owner who is no longer authenticated', () => {
    handoff.rememberHeld(orderId, owner);
    auth.status.set({ kind: 'authenticated', userId: otherOwner });
    TestBed.tick();
    handoff.rememberHeld(orderId, owner);
    auth.status.set({ kind: 'authenticated', userId: owner });
    expect(handoff.recentHeld(owner)).toBeNull();
    expect(handoff.takeOutcome(orderId, owner)).toBeNull();
  });

  it('rejects late Confirmed writes from an owner who is no longer authenticated', () => {
    auth.status.set({ kind: 'authenticated', userId: otherOwner });
    TestBed.tick();
    handoff.rememberConfirmed(orderId, owner);
    auth.status.set({ kind: 'authenticated', userId: owner });
    expect(handoff.takeOutcome(orderId, owner)).toBeNull();
  });

  it('does not return a B3 outcome after logout before the effect runs', () => {
    handoff.rememberConfirmed(orderId, owner);
    auth.status.set({ kind: 'anonymous' });
    expect(handoff.takeOutcome(orderId, owner)).toBeNull();
  });

  it('manual clear removes both the B3 outcome and the recent hold', () => {
    handoff.rememberHeld(orderId, owner);
    handoff.clear();
    expect(handoff.takeOutcome(orderId, owner)).toBeNull();
    expect(handoff.recentHeld(owner)).toBeNull();
    expect(handoff.recentHeldDeadline(owner)).toBeNull();
  });
});
