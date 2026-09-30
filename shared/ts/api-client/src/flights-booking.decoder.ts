import type { ConfirmedFlightOrderResponse, HeldFlightOrderResponse } from './flights-booking.types';

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
  if (!aggregateId(response['aggregateId']) || response['aggregateId'] !== expectedAggregateId) {
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
