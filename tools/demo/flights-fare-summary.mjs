import { createHash } from 'node:crypto';

const unknown = () => ({
  permission: 'Unknown',
  penalty: null,
  permissionReason: 'NotProvided',
  penaltyReason: 'NotProvided',
});
const allowed = (amount) => ({
  permission: 'Allowed',
  penalty: { amount, currency: 'RUB' },
  permissionReason: 'Known',
  penaltyReason: 'Known',
});
/** Fixed fictional facts, no model, supplier, credentials or control endpoint. */
export function demoFareSummary(quote, preset = 'mixed', mode) {
  const counts = quote.offer.itinerary.slices.map((s) => s.segments.length);
  const observed = new Date().toISOString();
  const change = preset === 'unknown' ? unknown() : allowed('0.00');
  const refund = preset === 'known' ? allowed('1250.50') : unknown();
  const legs = counts.map((_, leg) => ({
    leg,
    fareBrandName: leg % 2 ? 'Fictional Flex' : 'Fictional Basic',
    changeBeforeDeparture: leg % 2 ? unknown() : change,
  }));
  const passengers = counts.flatMap((count, leg) =>
    Array.from({ length: count }, (_, segment) =>
      Array.from({ length: quote.binding.passengerCount }, (_, adult) => ({
        leg,
        segment,
        adult,
        fareBasisCode: adult % 2 ? 'FICTIONAL-B' : 'FICTIONAL-A',
        cabinClassMarketing: 'economy',
        checkedBaggageQuantity: preset === 'unknown' || segment > 0 ? null : adult % 2,
        carryOnBaggageQuantity: preset === 'unknown' ? null : 1,
      })),
    ).flat(),
  );
  const stale = preset === 'expired';
  return {
    schemaVersion: 1,
    aggregateId: quote.aggregateId,
    offerId: quote.offer.id,
    quoteRevision: quote.binding.revision,
    fingerprint: createHash('sha256')
      .update(JSON.stringify({ legs, passengers, revision: quote.binding.revision }))
      .digest('hex'),
    state: stale ? 'Stale' : 'Current',
    reason: stale ? 'Expired' : null,
    provenance: 'QuotedOffer',
    sourceObservedAt: observed,
    offerExpiresAt: quote.offer.expiresAt,
    firstDepartureAt: quote.offer.itinerary.slices
      .flatMap((s) => s.segments)
      .map((s) => s.departAt)
      .sort((a, b) => Date.parse(a) - Date.parse(b))[0],
    serverObservedAt: stale ? new Date(Date.parse(quote.offer.expiresAt) + 1).toISOString() : observed,
    passengerCount: quote.binding.passengerCount,
    segmentCounts: counts,
    aiAvailable: true,
    aiStatus: mode === 'organized' ? 'InvalidOutput' : 'NotRequested',
    presentation: 'Deterministic',
    changeBeforeDeparture: change,
    refundBeforeDeparture: refund,
    sections: [],
    legs,
    passengers,
    missingInformation: ['Полный текст правил, условия после вылета и при неявке не предоставлены.'],
    notices: [
      'Сбор не определяет сумму возврата или зачисление денег.',
      ...(quote.purchase?.services.length
        ? ['Правила обмена и возврата дополнительных мест и багажа не предоставлены.']
        : []),
    ],
  };
}
export function historicalDemoFareSummary(summary) {
  return summary
    ? {
        ...structuredClone(summary),
        state: 'Stale',
        reason: 'Historical',
        provenance: 'Historical',
        aiAvailable: false,
        presentation: 'Deterministic',
        serverObservedAt: new Date().toISOString(),
      }
    : null;
}
