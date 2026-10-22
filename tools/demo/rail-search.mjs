// All records are authored fictional data; no provider network or retained history.
const generation = 'a'.repeat(32);
const epoch = 'b'.repeat(32);
const attribution = {
  frameUrl: '/rail-fictional-banner.html',
  text: 'Вымышленная демонстрация. Это не данные Яндекса.',
  url: 'https://rasp.yandex.ru/',
  width: 240,
  height: 130,
};
const stations = [
  ['s1', 'Учебная Северная'],
  ['s2', 'Учебная Южная'],
  ['s3', 'Учебная Восточная'],
].map(([code, name]) => ({
  reference: { source: 'yandex-rasp', code },
  name,
  settlement: 'Вымышленный город',
  region: 'Учебная область',
}));
export function fictionalRailSuggestions(q, now = Date.now(), preset = 'normal') {
  const receivedAt = new Date(now).toISOString();
  return {
    kind: 'complete',
    reasons: [],
    suggestions: stations.filter(
      (x) => x.name.toLowerCase().includes(q.toLowerCase()) || x.settlement.toLowerCase().includes(q.toLowerCase()),
    ),
    hasMore: false,
    catalogueGeneration: generation,
    source: 'yandex-rasp',
    profile: 'fictional',
    profileEpoch: epoch,
    receivedAt,
    expiresAt: new Date(now + (preset === 'catalogue-expiry' ? 1500 : 1800000)).toISOString(),
    attribution,
  };
}
export function fictionalRailSearch(request, now = Date.now(), preset = 'normal') {
  const receivedAt = new Date(now).toISOString();
  const modes = request.transport === 'all' ? ['train', 'suburban'] : [request.transport];
  const modeEvidence = modes.map((transport) => ({
    transport,
    kind: preset === 'failed' ? 'failed' : preset === 'interval' ? 'limited' : 'complete',
    reasons: preset === 'failed' ? ['supplier-http'] : preset === 'interval' ? ['interval-service-unsupported'] : [],
    originalCount: ['empty', 'interval', 'failed'].includes(preset) ? 0 : 1,
    normalizedCount: ['empty', 'interval', 'failed'].includes(preset) ? 0 : 1,
    receivedAt: preset === 'failed' ? null : receivedAt,
  }));
  const items = ['empty', 'interval', 'failed'].includes(preset)
    ? []
    : modes.map((transport) => {
        const departure = `${request.departureDate}T10:00:00+03:00`;
        const arrival = `${request.departureDate}T12:00:00+03:00`;
        const ticks = (x) => (BigInt(Date.parse(x)) * 10000n + 621355968000000000n).toString();
        return {
          key: [
            'yandex-rasp',
            `fictional-${transport}`,
            transport,
            request.from.code,
            request.to.code,
            ticks(departure),
            ticks(arrival),
          ].join('|'),
          threadReference: `fictional-${transport}`,
          transport,
          from: request.from,
          to: request.to,
          departure,
          arrival,
          durationSeconds: 7200,
          sourceReportedDate: null,
          number: transport === 'train' ? '001' : '6001',
          title: 'Вымышленный рейс',
          carrier: null,
          departurePlatform: null,
          arrivalPlatform: null,
          stops: null,
          tickets: {
            electronicTicket: 'false',
            electronicTicketReason: null,
            faresState: preset === 'partial' ? 'limited' : 'reported',
            fares: [
              {
                name: 'Учебный тип',
                price: { state: 'reported', amount: '9007199254740993.05', currency: 'RUB', reason: null, receivedAt },
              },
              ...(preset === 'partial'
                ? [
                    {
                      name: null,
                      price: {
                        state: 'unavailable',
                        amount: null,
                        currency: null,
                        reason: 'invalid-price',
                        receivedAt,
                      },
                    },
                  ]
                : []),
            ],
            reasons: preset === 'partial' ? ['invalid-fare'] : [],
            seatAvailability: 'unknown',
            operationalUpdates: 'not-provided',
            damaged: preset === 'partial',
          },
          receivedAt,
          reasons: [],
          source: 'yandex-rasp',
          departureTimeZone: null,
          arrivalTimeZone: null,
          sourceUpdatedAt: null,
        };
      });
  return {
    kind: preset === 'failed' ? 'failed' : preset === 'interval' ? 'limited' : 'complete',
    reasons: preset === 'failed' ? ['supplier-http'] : preset === 'interval' ? ['interval-service-unsupported'] : [],
    request,
    source: 'yandex-rasp',
    profile: 'fictional',
    profileEpoch: epoch,
    modes: modeEvidence,
    items,
    normalizedCount: items.length,
    receivedAt: preset === 'failed' ? null : receivedAt,
    expiresAt:
      preset === 'failed'
        ? null
        : new Date(
            now +
              (preset === 'result-expiry' ? 1500 : ['empty', 'partial', 'interval'].includes(preset) ? 30000 : 300000),
          ).toISOString(),
    completedAt: receivedAt,
    attribution: preset === 'failed' ? null : attribution,
  };
}
export function fictionalRailDetails(request, now = Date.now(), preset = 'normal') {
  const received = new Date(now).toISOString();
  const verifyFailed = preset === 'detail-recheck-failed';
  const noMatch = preset === 'detail-no-match';
  const routeFailed = preset === 'detail-route-failed';
  const quota = preset === 'detail-quota';
  const partial = preset === 'detail-partial';
  const verification = {
    transport: request.selection.transport,
    kind: verifyFailed ? 'failed' : 'complete',
    reasons: verifyFailed ? ['supplier-http'] : [],
    receivedAt: verifyFailed ? null : new Date(now - 1).toISOString(),
  };
  const missing = (reason) => ({ state: 'not-provided', value: null, reason });
  const reported = (value) => ({ state: 'reported', value, reason: null });
  const stops = [
    {
      sequence: 0,
      station: { ...request.search.from, name: 'Учебная Северная' },
      arrival: missing('terminal-not-provided'),
      departure: reported(request.selection.departure),
      dwellSeconds: missing('missing-dwell'),
      platform: missing('missing-platform'),
      reasons: [],
    },
    {
      sequence: 1,
      station: {
        source: 'yandex-rasp',
        code: 's900003',
        name: 'Учебная промежуточная станция с длинным вымышленным названием для проверки узкого экрана',
      },
      arrival: partial
        ? { state: 'unavailable', value: null, reason: 'invalid-time' }
        : reported(request.search.departureDate + 'T11:00:00+03:00'),
      departure: reported(request.search.departureDate + 'T11:01:00+03:00'),
      dwellSeconds: reported(60),
      platform: missing('missing-platform'),
      reasons: partial ? ['invalid-time'] : [],
    },
    {
      sequence: 2,
      station: { ...request.search.to, name: 'Учебная Южная' },
      arrival: reported(request.selection.arrival),
      departure: missing('terminal-not-provided'),
      dwellSeconds: reported(0),
      platform: missing('missing-platform'),
      reasons: [],
    },
  ];
  const unavailable = verifyFailed || noMatch || routeFailed || quota;
  return {
    kind: verifyFailed || routeFailed ? 'failed' : noMatch || quota ? 'skipped' : partial ? 'limited' : 'complete',
    reasons:
      verifyFailed || routeFailed
        ? ['supplier-http']
        : noMatch
          ? ['selection-not-confirmed']
          : quota
            ? ['quota']
            : partial
              ? ['invalid-time']
              : [],
    stage: verifyFailed || noMatch ? 'verification' : 'route',
    request,
    source: 'yandex-rasp',
    profile: 'fictional',
    profileEpoch: epoch,
    verification,
    routeReceivedAt: verifyFailed || noMatch || quota ? null : received,
    expiresAt: unavailable ? null : new Date(now + 60000).toISOString(),
    completedAt: received,
    stops: unavailable ? [] : stops,
    boardingIndex: unavailable ? null : 0,
    alightingIndex: unavailable ? null : 2,
    attribution: unavailable ? null : attribution,
  };
}
export function createRailDemo() {
  let preset = 'normal';
  return async (request, response, url) => {
    const control = url.pathname === '/api/demo/rail/preset';
    if (!url.pathname.startsWith('/api/rail/') && !control) return false;
    response.setHeader('Cache-Control', 'no-store');
    response.setHeader('Content-Type', 'application/json');
    const send = (status, body) => {
      response.writeHead(status);
      response.end(JSON.stringify(body));
    };
    let body = '';
    for await (const chunk of request) {
      body += chunk;
      if (Buffer.byteLength(body) > 8192) {
        send(413, { code: 'Rail.Request.TooLarge' });
        return true;
      }
    }
    if (control && request.method === 'POST') {
      let value;
      try {
        value = JSON.parse(body);
      } catch {
        send(400, {});
        return true;
      }
      if (
        ![
          'normal',
          'empty',
          'interval',
          'failed',
          'partial',
          'result-expiry',
          'catalogue-expiry',
          'stale',
          'detail-partial',
          'detail-no-match',
          'detail-recheck-failed',
          'detail-route-failed',
          'detail-quota',
        ].includes(value.preset)
      ) {
        send(400, {});
        return true;
      }
      preset = value.preset;
      send(200, { preset });
      return true;
    }
    if (request.method === 'GET' && url.pathname === '/api/rail/stations') {
      const q = url.searchParams.get('q') ?? '';
      if (q.length < 3 || q.length > 80) {
        send(400, { code: 'Rail.StationQuery.Invalid' });
        return true;
      }
      send(200, fictionalRailSuggestions(q, Date.now(), preset));
      return true;
    }
    if (request.method === 'POST' && url.pathname === '/api/rail/search') {
      let value;
      try {
        value = JSON.parse(body);
      } catch {
        send(400, { code: 'Rail.Request.Invalid' });
        return true;
      }
      if (preset === 'stale' || value.catalogueGeneration !== generation) {
        send(400, { code: 'Rail.StationSelection.Expired' });
        return true;
      }
      if (
        !['train', 'suburban', 'all'].includes(value.transport) ||
        !/^\d{4}-\d{2}-\d{2}$/.test(value.departureDate) ||
        !stations.some((x) => x.reference.code === value.from?.code) ||
        !stations.some((x) => x.reference.code === value.to?.code) ||
        value.from.code === value.to.code
      ) {
        send(400, { code: 'Rail.Request.Invalid' });
        return true;
      }
      send(200, fictionalRailSearch(value, Date.now(), preset));
      return true;
    }
    if (request.method === 'POST' && url.pathname === '/api/rail/journey-details') {
      let value;
      try {
        value = JSON.parse(body);
      } catch {
        send(400, { code: 'Rail.Request.Invalid' });
        return true;
      }
      if (
        !value.search ||
        !value.selection ||
        value.expectedProfileEpoch !== epoch ||
        value.search.catalogueGeneration !== generation
      ) {
        send(400, { code: 'Rail.Selection.Invalid' });
        return true;
      }
      send(200, fictionalRailDetails(value, Date.now(), preset));
      return true;
    }
    send(404, {});
    return true;
  };
}
