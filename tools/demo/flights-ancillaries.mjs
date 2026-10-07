import { readFileSync } from 'node:fs';

const fixture = JSON.parse(
  readFileSync(new URL('../../tests/fixtures/flights-ancillaries.json', import.meta.url), 'utf8'),
);
export const ancillaryPresets = ['purchase-success', 'purchase-diff', 'purchase-unknown'];
const money = (cents, currency) => ({
  amount: `${cents / 100n}.${(cents % 100n).toString().padStart(2, '0')}`,
  currency,
});
const cents = (value) => {
  if (typeof value !== 'string' || !/^\d+(\.\d{1,2})?$/.test(value)) throw new TypeError('Invalid fictional money');
  const [whole, fraction = ''] = value.split('.');
  return BigInt(whole) * 100n + BigInt(fraction.padEnd(2, '0'));
};
export function buildDemoAncillaryCatalog(quote, includeSeats) {
  const currency = quote.offer.currency;
  const scope = quote.offer.itinerary.slices.flatMap((s, leg) => s.segments.map((_, segment) => ({ leg, segment })));
  const metadata = fixture.data.available_services[0].metadata;
  const limits = {
    maximumWeightKg: metadata.maximum_weight_kg,
    maximumHeightCm: metadata.maximum_height_cm ?? null,
    maximumDepthCm: metadata.maximum_depth_cm ?? null,
    maximumLengthCm: metadata.maximum_length_cm ?? null,
  };
  const services = quote.binding.slots.map((slot) => ({
    selectionKey: `bag_${slot.bookingPassengerId}`,
    kind: 'checked-baggage',
    bookingPassengerIds: [slot.bookingPassengerId],
    segments: scope,
    unitPrice: { amount: '10.00', currency },
    maximumQuantity: 2,
    selectable: true,
    reason: null,
    name: 'Вымышленный дополнительный багаж',
    seatDesignator: null,
    physicalSeat: null,
    disclosures: null,
    baggage: limits,
  }));
  const seatMaps = includeSeats
    ? scope.map((address) => {
        const elements = ['12A', '12C'].map((designator, index) => {
          const keys = [];
          for (const slot of quote.binding.slots) {
            const key = `seat_${address.leg}_${address.segment}_${designator}_${slot.bookingPassengerId}`;
            keys.push(key);
            services.push({
              selectionKey: key,
              kind: 'seat',
              bookingPassengerIds: [slot.bookingPassengerId],
              segments: [address],
              unitPrice: { amount: index === 0 ? '0.00' : '3.50', currency },
              maximumQuantity: 1,
              selectable: true,
              reason: null,
              name: 'Вымышленное место',
              seatDesignator: designator,
              physicalSeat: `deck0-cabin0:${designator}`,
              disclosures: ['Fictional seat conditions.'],
              baggage: null,
            });
          }
          return { kind: 'seat', designator, serviceKeys: keys };
        });
        return {
          segment: address,
          cabins: [
            {
              deck: 0,
              cabin: quote.offer.itinerary.slices[address.leg].segments[address.segment].cabinClass,
              rows: [
                {
                  sections: [
                    { elements: [elements[0], { kind: 'empty', designator: null, serviceKeys: [] }, elements[1]] },
                  ],
                },
              ],
            },
          ],
        };
      })
    : [];
  return {
    aggregateId: quote.aggregateId,
    quoteRevision: quote.binding.revision,
    baseFare: quote.purchase?.baseFare ?? { amount: Number(quote.offer.totalAmount).toFixed(2), currency },
    expiresAt: quote.offer.expiresAt,
    services,
    allowances: quote.binding.slots.flatMap((slot, i) =>
      scope.map((a) => ({
        bookingPassengerId: slot.bookingPassengerId,
        segment: a,
        checkedQuantity: a.segment > 0 ? null : i === 0 ? 1 : 0,
        carryOnQuantity: a.segment > 0 ? null : 1,
      })),
    ),
    seatMaps,
    seatsUnavailable: false,
    unsupportedPricing: false,
  };
}
export function quoteDemoPurchase(quote, selections, previous) {
  const requested =
    selections ??
    previous?.purchase?.services.map((s) => ({ selectionKey: s.selectionKey, quantity: s.quantity })) ??
    [];
  if (
    !Array.isArray(requested) ||
    requested.length > 256 ||
    new Set(requested.map((s) => s?.selectionKey)).size !== requested.length
  )
    throw new TypeError('Invalid fictional selection');
  const catalog = buildDemoAncillaryCatalog(quote, true);
  const services = [];
  const occupied = new Set();
  const passengerSeats = new Set();
  for (const selection of requested) {
    const service = catalog.services.find((s) => s.selectionKey === selection?.selectionKey);
    if (
      !service ||
      !Number.isInteger(selection.quantity) ||
      selection.quantity < 1 ||
      selection.quantity > service.maximumQuantity
    )
      throw new TypeError('Unavailable fictional service');
    if (service.kind === 'seat') {
      const scope = JSON.stringify(service.segments);
      const physical = scope + service.physicalSeat;
      const person = scope + service.bookingPassengerIds[0];
      if (occupied.has(physical) || passengerSeats.has(person)) throw new TypeError('Seat conflict');
      occupied.add(physical);
      passengerSeats.add(person);
    }
    services.push({
      selectionKey: service.selectionKey,
      kind: service.kind,
      bookingPassengerId: service.bookingPassengerIds[0],
      segments: structuredClone(service.segments),
      quantity: selection.quantity,
      lineTotal: money(cents(service.unitPrice.amount) * BigInt(selection.quantity), quote.offer.currency),
      seatDesignator: service.seatDesignator,
      disclosures: service.disclosures,
      baggage: service.baggage,
    });
  }
  const base = cents(catalog.baseFare.amount);
  const extras = services.reduce((sum, s) => sum + cents(s.lineTotal.amount), 0n);
  quote.purchase = {
    quoteRevision: quote.binding.revision,
    baseFare: money(base, quote.offer.currency),
    extras: money(extras, quote.offer.currency),
    total: money(base + extras, quote.offer.currency),
    services,
    expiresAt: quote.offer.expiresAt,
    noticeVersion: 'booking-services-v1',
  };
  quote.offer.totalAmount = Number(base + extras) / 100;
  if (previous) {
    quote.priceChanged =
      previous.offer.totalAmount !== quote.offer.totalAmount || previous.offer.currency !== quote.offer.currency;
    quote.oldAmount = quote.priceChanged ? previous.offer.totalAmount : null;
    quote.oldCurrency = quote.priceChanged ? previous.offer.currency : null;
    quote.newAmount = quote.priceChanged ? quote.offer.totalAmount : null;
    quote.newCurrency = quote.priceChanged ? quote.offer.currency : null;
  }
  return quote;
}
export function demoCreationStatus(id, quote, record, order) {
  const status = order?.status ?? 'OfferQuoted';
  return {
    aggregateId: id,
    state: record?.state ?? 'NotStarted',
    bookingStatus: status,
    passengerCount: quote.binding.passengerCount,
    bookingPassengerIds: quote.binding.slots.map((s) => s.bookingPassengerId),
    itinerary: quote.offer.itinerary,
    accepted: record?.accepted ?? quote.purchase ?? null,
    actual: record?.actual ?? null,
    heldUntil: status === 'Held' ? (record?.heldUntil ?? null) : null,
    canConfirm: record?.state === 'Matches' && status === 'Held',
    canCancel: !!order && ['Held', 'Confirmed', 'Ticketed'].includes(status),
    canRefresh: !record,
    observedAt: new Date().toISOString(),
  };
}
