import { decodeFlightFareSummary, optionalFlightFareSummary } from './flights-fare-summary.decoder';

const id = '11111111-1111-4111-8111-111111111111';
const known = {
  permission: 'Allowed',
  penalty: { amount: '0.00', currency: 'RUB' },
  permissionReason: 'Known',
  penaltyReason: 'Known',
};
const unknown = { permission: 'Unknown', penalty: null, permissionReason: 'NotProvided', penaltyReason: 'NotProvided' };
const binding = { aggregateId: id, offerId: id, quoteRevision: id, passengerCount: 1, segmentCounts: [1] };
function summary() {
  return {
    schemaVersion: 1,
    aggregateId: id,
    offerId: id,
    quoteRevision: id,
    fingerprint: 'a'.repeat(64),
    state: 'Current',
    reason: null,
    provenance: 'QuotedOffer',
    sourceObservedAt: '2037-01-01T00:00:00Z',
    serverObservedAt: '2037-01-01T00:01:00Z',
    offerExpiresAt: '2037-01-01T00:30:00Z',
    firstDepartureAt: '2037-02-01T00:00:00Z',
    passengerCount: 1,
    segmentCounts: [1],
    aiAvailable: false,
    aiStatus: 'Disabled',
    presentation: 'Deterministic',
    changeBeforeDeparture: known,
    refundBeforeDeparture: unknown,
    sections: [],
    legs: [{ leg: 0, fareBrandName: 'Economy brand', changeBeforeDeparture: known }],
    passengers: [
      {
        leg: 0,
        segment: 0,
        adult: 0,
        fareBasisCode: null,
        cabinClassMarketing: null,
        checkedBaggageQuantity: 0,
        carryOnBaggageQuantity: null,
      },
    ],
    missingInformation: ['Полный текст не предоставлен'],
    notices: ['Сбор не определяет сумму возврата'],
  };
}
describe('fare summary contract', () => {
  it.each(['79228162514264337593543950335', '0.0000000000000000000000000001'])(
    'retains exact decimal boundary %s',
    (amount) => {
      const value = { ...summary(), changeBeforeDeparture: { ...known, penalty: { amount, currency: 'RUB' } } };
      expect(decodeFlightFareSummary(value, binding).changeBeforeDeparture?.penalty?.amount).toBe(amount);
    },
  );
  it.each(['79228162514264337593543950336', '79228162514264337593543950335.1', '0.00000000000000000000000000001'])(
    'rejects decimal overflow %s',
    (amount) => {
      expect(() =>
        decodeFlightFareSummary(
          { ...summary(), changeBeforeDeparture: { ...known, penalty: { amount, currency: 'RUB' } } },
          binding,
        ),
      ).toThrow();
    },
  );
  it.each([100, 2147483647])('accepts informational baggage quantity %s independent of purchase limits', (quantity) => {
    const value = summary();
    value.passengers[0].checkedBaggageQuantity = quantity;
    expect(decodeFlightFareSummary(value, binding).passengers[0]?.checkedBaggageQuantity).toBe(quantity);
  });
  it('rejects Int32 informational baggage overflow', () => {
    const value = summary();
    value.passengers[0].checkedBaggageQuantity = 2147483648;
    expect(() => decodeFlightFareSummary(value, binding)).toThrow();
  });
  it('preserves exact zero money, unknown and explicit zero baggage', () => {
    const value = decodeFlightFareSummary(summary(), binding);
    expect(value.changeBeforeDeparture?.penalty?.amount).toBe('0.00');
    expect(value.passengers[0]?.checkedBaggageQuantity).toBe(0);
    expect(value.passengers[0]?.carryOnBaggageQuantity).toBeNull();
  });
  it('keeps absent legacy evidence unavailable', () =>
    expect(optionalFlightFareSummary(undefined, binding)).toEqual({ summary: null }));
  it.each([
    { fingerprint: 'bad' },
    { quoteRevision: '22222222-2222-4222-8222-222222222222' },
    { segmentCounts: [2] },
    { refundBeforeDeparture: { ...unknown, permission: 'Allowed' } },
    { changeBeforeDeparture: { ...known, penalty: { amount: 0, currency: 'RUB' } } },
    { serverObservedAt: '2037-02-02T00:00:00Z' },
  ])('suppresses malformed or mismatched evidence %p', (patch) =>
    expect(optionalFlightFareSummary({ ...summary(), ...patch }, binding)).toMatchObject({
      summary: null,
      notice: expect.any(String),
    }),
  );
  it('never promotes stale history', () => {
    const value = { ...summary(), state: 'Stale', reason: 'Historical', provenance: 'Historical' };
    expect(decodeFlightFareSummary(value, { ...binding, historical: true }).state).toBe('Stale');
    expect(() => decodeFlightFareSummary(summary(), { ...binding, historical: true })).toThrow();
  });
  it('validates every pair for four legs and nine adults', () => {
    const value = {
      ...summary(),
      passengerCount: 9,
      segmentCounts: [1, 1, 1, 1],
      legs: Array.from({ length: 4 }, (_, leg) => ({ leg, fareBrandName: null, changeBeforeDeparture: unknown })),
      passengers: Array.from({ length: 4 }, (_, leg) =>
        Array.from({ length: 9 }, (_, adult) => ({ ...summary().passengers[0], leg, adult })),
      ).flat(),
    };
    expect(
      decodeFlightFareSummary(value, { ...binding, passengerCount: 9, segmentCounts: [1, 1, 1, 1] }).passengers,
    ).toHaveLength(36);
    value.passengers.pop();
    expect(() =>
      decodeFlightFareSummary(value, { ...binding, passengerCount: 9, segmentCounts: [1, 1, 1, 1] }),
    ).toThrow();
  });
});
