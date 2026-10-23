import type { RailJourneyDetailsRequest, RailJourneyDetailsResult } from '@travel/api-client';
import { railFixture } from './rail-fixture';
export function railDetailsFixture(now = Date.parse('2026-10-20T12:00:00Z')) {
  const { request: search, result } = railFixture(now);
  const selected = result.items[0];
  const request: RailJourneyDetailsRequest = {
    search,
    expectedProfileEpoch: result.profileEpoch,
    selection: {
      key: selected.key,
      transport: selected.transport,
      departure: selected.departure,
      arrival: selected.arrival,
    },
  };
  const missing = (reason: string) => ({ state: 'not-provided' as const, value: null, reason });
  const report = (value: string) => ({ state: 'reported' as const, value, reason: null });
  const reply: RailJourneyDetailsResult = {
    kind: 'complete',
    reasons: [],
    stage: 'route',
    request,
    source: 'yandex-rasp',
    profile: 'fictional',
    profileEpoch: result.profileEpoch,
    verification: { transport: 'train', kind: 'complete', reasons: [], receivedAt: new Date(now).toISOString() },
    routeReceivedAt: new Date(now).toISOString(),
    expiresAt: new Date(now + 60000).toISOString(),
    completedAt: new Date(now).toISOString(),
    stops: [
      {
        sequence: 0,
        station: { ...search.from, name: 'Учебная A' },
        arrival: missing('terminal-not-provided'),
        departure: report(selected.departure),
        dwellSeconds: missing('missing-dwell'),
        platform: missing('missing-platform'),
        reasons: [],
      },
      {
        sequence: 1,
        station: { ...search.to, name: 'Учебная Б' },
        arrival: report(selected.arrival),
        departure: missing('terminal-not-provided'),
        dwellSeconds: { state: 'reported', value: 0, reason: null },
        platform: missing('missing-platform'),
        reasons: [],
      },
    ],
    boardingIndex: 0,
    alightingIndex: 1,
    attribution: result.attribution,
  };
  return {
    request,
    reply,
    now,
    authority: {
      profile: result.profile,
      profileEpoch: result.profileEpoch,
      expiresAt: new Date(now + 1800000).toISOString(),
    },
  };
}
