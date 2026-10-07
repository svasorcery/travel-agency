import { decodeFlightPurchase } from './flights-ancillaries.decoder';
import type { FlightQuoteResponse } from './flights-quote.types';
import { decodeFlightSearchResponse } from './flights-search.decoder';
import type { FlightOffer } from './flights-search.types';

export class FlightQuoteContractError extends Error {
  constructor(field: string) {
    super(`Invalid Flights quote response at ${field}`);
    this.name = 'FlightQuoteContractError';
  }
}

function record(value: unknown, field: string): Record<string, unknown> {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) {
    throw new FlightQuoteContractError(field);
  }
  return value as Record<string, unknown>;
}

function optionalText(value: unknown, field: string): void {
  if (value !== null && (typeof value !== 'string' || value.trim().length === 0)) {
    throw new FlightQuoteContractError(field);
  }
}

function money(value: unknown, currency: unknown, field: string): void {
  if (typeof value !== 'number' || !Number.isFinite(value) || value < 0) {
    throw new FlightQuoteContractError(field);
  }
  if (typeof currency !== 'string' || !/^[A-Z]{3}$/.test(currency)) {
    throw new FlightQuoteContractError(`${field}Currency`);
  }
}

export function decodeFlightQuoteResponse(value: unknown): FlightQuoteResponse {
  const quote = record(value, 'response');
  if (
    typeof quote['aggregateId'] !== 'string' ||
    !/^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(quote['aggregateId']) ||
    quote['aggregateId'] === '00000000-0000-0000-0000-000000000000'
  ) {
    throw new FlightQuoteContractError('aggregateId');
  }

  let offer: FlightOffer | undefined;
  try {
    offer = decodeFlightSearchResponse({ offers: [quote['offer']], partialFailures: [], skippedProviders: [] })
      .offers[0];
  } catch {
    throw new FlightQuoteContractError('offer');
  }
  if (!offer || offer.providerOfferRef === null) throw new FlightQuoteContractError('offer.variant');

  const binding = record(quote['binding'], 'binding');
  const guid = (value: unknown): value is string =>
    typeof value === 'string' &&
    /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(value) &&
    value !== '00000000-0000-0000-0000-000000000000';
  const date = binding['firstDepartureLocalDate'];
  const parsed = typeof date === 'string' ? new Date(`${date}T00:00:00Z`) : new Date(NaN);
  if (
    !guid(binding['revision']) ||
    binding['passengerCount'] !== offer.passengerCount ||
    typeof date !== 'string' ||
    !/^\d{4}-\d{2}-\d{2}$/.test(date) ||
    date < '0001-01-01' ||
    !Number.isFinite(parsed.getTime()) ||
    parsed.toISOString().slice(0, 10) !== date ||
    !Array.isArray(binding['slots']) ||
    binding['slots'].length !== offer.passengerCount
  )
    throw new FlightQuoteContractError('binding');
  const ids = new Set<string>();
  for (const slot of binding['slots']) {
    const row = record(slot, 'binding.slots');
    if (!guid(row['bookingPassengerId']) || row['kind'] !== 'adult' || ids.has(row['bookingPassengerId'].toLowerCase()))
      throw new FlightQuoteContractError('binding.slots');
    ids.add(row['bookingPassengerId'].toLowerCase());
  }
  const fare = record(quote['fareConditions'], 'fareConditions');
  if (typeof fare['changeAllowed'] !== 'boolean' || typeof fare['refundAllowed'] !== 'boolean') {
    throw new FlightQuoteContractError('fareConditions.permissions');
  }
  optionalText(fare['fareBasisCode'], 'fareConditions.fareBasisCode');
  optionalText(fare['cabinClassMarketing'], 'fareConditions.cabinClassMarketing');
  for (const field of ['checkedBaggageQuantity', 'carryOnBaggageQuantity']) {
    if (typeof fare[field] !== 'number' || !Number.isSafeInteger(fare[field]) || fare[field] < 0) {
      throw new FlightQuoteContractError(`fareConditions.${field}`);
    }
  }

  if (typeof quote['priceChanged'] !== 'boolean') throw new FlightQuoteContractError('priceChanged');
  if (quote['priceChanged']) {
    money(quote['oldAmount'], quote['oldCurrency'], 'oldAmount');
    money(quote['newAmount'], quote['newCurrency'], 'newAmount');
    if (quote['newAmount'] !== offer.totalAmount || quote['newCurrency'] !== offer.currency) {
      throw new FlightQuoteContractError('newAmount');
    }
  } else if (
    quote['oldAmount'] !== null ||
    quote['oldCurrency'] !== null ||
    quote['newAmount'] !== null ||
    quote['newCurrency'] !== null
  ) {
    throw new FlightQuoteContractError('priceDelta');
  }
  const response = value as FlightQuoteResponse;
  if (quote['purchase'] !== undefined && quote['purchase'] !== null) {
    const purchase = decodeFlightPurchase(
      quote['purchase'],
      binding['revision'] as string,
      ids,
      offer.itinerary.slices.map((s) => s.segments.length),
    );
    if (purchase.total.currency !== offer.currency || Number(purchase.total.amount) !== offer.totalAmount)
      throw new FlightQuoteContractError('purchase.total');
  }
  return {
    ...response,
    binding: {
      ...response.binding,
      revision: response.binding.revision.toLowerCase(),
      slots: response.binding.slots.map((slot) => ({
        ...slot,
        bookingPassengerId: slot.bookingPassengerId.toLowerCase(),
      })),
    },
  };
}
