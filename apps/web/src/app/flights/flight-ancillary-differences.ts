import type { FlightCreationStatus, FlightPurchasedService } from '@travel/api-client';
import { sameFlightMoney } from '@travel/api-client';

function scope(service: FlightPurchasedService): string {
  return `${service.kind}:${service.bookingPassengerId}:${service.segments
    .map((s) => `${s.leg}:${s.segment}`)
    .sort()
    .join(',')}`;
}
function describe(service: FlightPurchasedService, passengerIds: string[]): string {
  const index = passengerIds.indexOf(service.bookingPassengerId);
  const person = index >= 0 ? `Пассажир ${index + 1}` : 'Пассажир';
  const route = service.segments.map((s) => `участок ${s.leg + 1}, сегмент ${s.segment + 1}`).join('; ');
  return `${person}, ${route}: ${service.kind === 'seat' ? `место ${service.seatDesignator}` : `багаж × ${service.quantity}`}`;
}
function sameTerms(selected: FlightPurchasedService, actual: FlightPurchasedService): boolean {
  return (
    selected.quantity === actual.quantity &&
    selected.seatDesignator === actual.seatDesignator &&
    sameFlightMoney(selected.lineTotal, actual.lineTotal) &&
    JSON.stringify(selected.disclosures) === JSON.stringify(actual.disclosures) &&
    JSON.stringify(selected.baggage) === JSON.stringify(actual.baggage)
  );
}
export function flightAncillaryDifferences(value: FlightCreationStatus): string[] {
  if (value.actual === null || value.accepted === null) return ['Фактические услуги пока не подтверждены.'];
  const differences: string[] = [];
  const remaining = [...value.actual.services];
  for (const selected of value.accepted.services) {
    const candidates = remaining.filter((s) => scope(s) === scope(selected));
    // Several bag products can cover the same adult and segments. Match retained
    // terms first; supplier line IDs and order need not match the selection.
    const actual =
      candidates.find((s) => sameTerms(selected, s)) ?? (candidates.length === 1 ? candidates[0] : undefined);
    if (!actual) {
      differences.push(
        `Выбрано: ${describe(selected, value.bookingPassengerIds)}. В заказе эта строка не подтверждена.`,
      );
      continue;
    }
    remaining.splice(remaining.indexOf(actual), 1);
    if (!sameTerms(selected, actual)) {
      differences.push(
        `Выбрано: ${describe(selected, value.bookingPassengerIds)} · ${selected.lineTotal.amount} ${selected.lineTotal.currency}. В заказе: ${describe(actual, value.bookingPassengerIds)} · ${actual.lineTotal.amount} ${actual.lineTotal.currency}. Проверьте также ограничения услуги.`,
      );
    }
  }
  for (const actual of remaining)
    differences.push(
      `В заказе дополнительная строка: ${describe(actual, value.bookingPassengerIds)} · ${actual.lineTotal.amount} ${actual.lineTotal.currency}.`,
    );
  if (!sameFlightMoney(value.accepted.total, value.actual.total))
    differences.push(
      `Выбранный итог: ${value.accepted.total.amount} ${value.accepted.total.currency}. Фактический итог: ${value.actual.total.amount} ${value.actual.total.currency}.`,
    );
  return differences;
}
