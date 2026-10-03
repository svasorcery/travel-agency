import type {
  CancelledFlightOrderResponse,
  ConfirmedFlightOrderResponse,
  FlightOrderListResponse,
  FlightOrderResponse,
  HeldFlightOrderResponse,
} from './flights-booking.types';
import { decodeFlightItinerary } from './flights-search.decoder';

const GUID = /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i;

export class FlightBookingContractError extends Error {
  constructor(field: string) {
    super(`Invalid Flights booking response at ${field}`);
    this.name = 'FlightBookingContractError';
  }
}

function record(value: unknown, field: string): Record<string, unknown> {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) {
    throw new FlightBookingContractError(field);
  }
  return value as Record<string, unknown>;
}

function aggregateId(value: unknown): value is string {
  return typeof value === 'string' && GUID.test(value) && value !== '00000000-0000-0000-0000-000000000000';
}

function sameAggregateId(response: Record<string, unknown>, expectedAggregateId: string, field: string): void {
  if (
    !aggregateId(response['aggregateId']) ||
    response['aggregateId'].toLowerCase() !== expectedAggregateId.toLowerCase()
  ) {
    throw new FlightBookingContractError(field);
  }
}

export function decodeHeldOrderResponse(value: unknown, expectedAggregateId?: string): HeldFlightOrderResponse {
  const response = record(value, 'response');
  if (!aggregateId(response['aggregateId'])) throw new FlightBookingContractError('aggregateId');
  if (expectedAggregateId !== undefined) sameAggregateId(response, expectedAggregateId, 'aggregateId');
  if (typeof response['providerOrderId'] !== 'string' || response['providerOrderId'].trim() === '') {
    throw new FlightBookingContractError('providerOrderId');
  }
  if (
    typeof response['heldUntil'] !== 'string' ||
    !/(?:Z|[+-]\d{2}:\d{2})$/i.test(response['heldUntil']) ||
    Number.isNaN(Date.parse(response['heldUntil']))
  ) {
    throw new FlightBookingContractError('heldUntil');
  }
  return value as HeldFlightOrderResponse;
}

export function decodeConfirmedOrderResponse(
  value: unknown,
  expectedAggregateId?: string,
): ConfirmedFlightOrderResponse {
  const response = record(value, 'response');
  if (!aggregateId(response['aggregateId'])) throw new FlightBookingContractError('aggregateId');
  if (expectedAggregateId !== undefined) sameAggregateId(response, expectedAggregateId, 'aggregateId');
  if (response['status'] !== 'Confirmed') throw new FlightBookingContractError('status');
  if (response['paymentRef'] !== null && typeof response['paymentRef'] !== 'string') {
    throw new FlightBookingContractError('paymentRef');
  }
  return value as ConfirmedFlightOrderResponse;
}

function timestamp(value: unknown, field: string): void {
  const calendarDate = typeof value === 'string' ? value.slice(0, 10) : '';
  const midnight = Date.parse(`${calendarDate}T00:00:00Z`);
  if (
    typeof value !== 'string' ||
    !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$/i.test(value) ||
    !Number.isFinite(Date.parse(value)) ||
    !Number.isFinite(midnight) ||
    new Date(midnight).toISOString().slice(0, 10) !== calendarDate
  ) {
    throw new FlightBookingContractError(field);
  }
}

export function decodeFlightOrderResponse(value: unknown, expectedAggregateId: string): FlightOrderResponse {
  const response = record(value, 'response');
  sameAggregateId(response, expectedAggregateId, 'aggregateId');
  if (
    !Number.isInteger(response['passengerCount']) ||
    (response['passengerCount'] as number) < 1 ||
    (response['passengerCount'] as number) > 9
  )
    throw new FlightBookingContractError('passengerCount');
  if (!['Held', 'Confirmed', 'Ticketed', 'Cancelled', 'Refunded'].includes(response['status'] as string)) {
    throw new FlightBookingContractError('status');
  }
  if (
    typeof response['totalAmount'] !== 'number' ||
    !Number.isFinite(response['totalAmount']) ||
    response['totalAmount'] < 0
  ) {
    throw new FlightBookingContractError('totalAmount');
  }
  if (typeof response['currency'] !== 'string' || !/^[A-Z]{3}$/.test(response['currency'])) {
    throw new FlightBookingContractError('currency');
  }
  try {
    decodeFlightItinerary(response['itinerary']);
  } catch {
    throw new FlightBookingContractError('itinerary');
  }
  if (
    !Array.isArray(response['ticketNumbers']) ||
    response['ticketNumbers'].some((number) => typeof number !== 'string' || number.trim() === '')
  ) {
    throw new FlightBookingContractError('ticketNumbers');
  }
  if (response['status'] === 'Ticketed' && response['ticketNumbers'].length === 0) {
    throw new FlightBookingContractError('ticketNumbers');
  }
  timestamp(response['bookedAt'], 'bookedAt');
  for (const field of ['ticketedAt', 'cancelledAt', 'refundedAt']) {
    if (response[field] !== null) timestamp(response[field], field);
  }
  return value as FlightOrderResponse;
}

export function decodeFlightOrderListResponse(value: unknown, expectedOffset: number): FlightOrderListResponse {
  const response = record(value, 'response');
  if (response['limit'] !== 21) throw new FlightBookingContractError('limit');
  if (
    !Number.isSafeInteger(response['offset']) ||
    (response['offset'] as number) < 0 ||
    response['offset'] !== expectedOffset
  ) {
    throw new FlightBookingContractError('offset');
  }
  const items = response['items'];
  if (!Array.isArray(items) || items.length > 21) throw new FlightBookingContractError('items');
  const seen = new Set<string>();
  for (const [index, item] of items.entries()) {
    const order = record(item, `items[${index}]`);
    const id = order['aggregateId'];
    if (!aggregateId(id) || seen.has(id.toLowerCase())) {
      throw new FlightBookingContractError(`items[${index}].aggregateId`);
    }
    decodeFlightOrderResponse(item, id);
    seen.add(id.toLowerCase());
  }
  return value as FlightOrderListResponse;
}

export function decodeCancelledOrderResponse(
  value: unknown,
  expectedAggregateId: string,
): CancelledFlightOrderResponse {
  const response = decodeFlightOrderResponse(value, expectedAggregateId);
  if (response.status !== 'Cancelled' && response.status !== 'Refunded') throw new FlightBookingContractError('status');
  if (response.status === 'Cancelled' && response.cancelledAt === null)
    throw new FlightBookingContractError('cancelledAt');
  if (response.status === 'Refunded' && response.refundedAt === null)
    throw new FlightBookingContractError('refundedAt');
  return response as CancelledFlightOrderResponse;
}
