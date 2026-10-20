import type { RailJourney, RailRequest, RailSearchResult } from '../src/rail.types';
export function railFixture(now = Date.parse('2026-10-20T12:00:00Z')): {
  request: RailRequest;
  result: RailSearchResult;
} {
  const date = new Date(now).toISOString().slice(0, 10);
  const receivedAt = new Date(now).toISOString();
  const request: RailRequest = {
    from: { source: 'yandex-rasp', code: 's1' },
    to: { source: 'yandex-rasp', code: 's2' },
    catalogueGeneration: 'a'.repeat(32),
    departureDate: date,
    transport: 'all',
  };
  const items: RailJourney[] = (['train', 'suburban'] as const).map((transport) => ({
    key: `fixture-${transport}`,
    threadReference: `fixture-${transport}`,
    transport,
    from: request.from,
    to: request.to,
    departure: `${date}T10:00:00+03:00`,
    arrival: `${date}T12:00:00+03:00`,
    durationSeconds: 7200,
    sourceReportedDate: null,
    number: '001',
    title: 'Вымышленный поезд',
    carrier: null,
    departurePlatform: null,
    arrivalPlatform: null,
    stops: null,
    tickets: {
      electronicTicket: 'false',
      electronicTicketReason: null,
      faresState: 'reported',
      fares: [
        {
          name: null,
          price: { state: 'reported', amount: '9007199254740993.05', currency: 'RUB', reason: null, receivedAt },
        },
      ],
      reasons: [],
      seatAvailability: 'unknown',
      operationalUpdates: 'not-provided',
      damaged: false,
    },
    receivedAt,
    reasons: [],
    source: 'yandex-rasp',
    departureTimeZone: null,
    arrivalTimeZone: null,
    sourceUpdatedAt: null,
  }));
  return {
    request,
    result: {
      kind: 'complete',
      reasons: [],
      request,
      source: 'yandex-rasp',
      profile: 'fictional',
      profileEpoch: 'b'.repeat(32),
      modes: items.map((x) => ({
        transport: x.transport,
        kind: 'complete',
        reasons: [],
        originalCount: 1,
        normalizedCount: 1,
        receivedAt,
      })),
      items,
      normalizedCount: 2,
      receivedAt,
      expiresAt: new Date(now + 300000).toISOString(),
      completedAt: receivedAt,
      attribution: {
        frameUrl: '/rail-fictional-banner.html',
        text: 'Вымышленные данные',
        url: 'https://rasp.yandex.ru/',
        width: 240,
        height: 130,
      },
    },
  };
}
