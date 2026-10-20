import { describe, expect, it } from 'vitest';
import { railFixture } from '../../../../tests/fixtures/rail-schedule';
import { decodeRailSearch, decodeRailSuggestions } from './rail.decoder';
export const railRequest = {
  from: { source: 'yandex-rasp' as const, code: 's1' },
  to: { source: 'yandex-rasp' as const, code: 's2' },
  catalogueGeneration: 'a'.repeat(32),
  departureDate: '2026-10-20',
  transport: 'all' as const,
};
const now = Date.parse('2026-10-20T12:00:00Z');
const receivedAt = '2026-10-20T12:00:00+00:00';
export function railResult() {
  return {
    kind: 'complete',
    reasons: [],
    request: railRequest,
    source: 'yandex-rasp',
    profile: 'fictional',
    profileEpoch: 'b'.repeat(32),
    modes: ['train', 'suburban'].map((transport) => ({
      transport,
      kind: 'complete',
      reasons: [],
      originalCount: 0,
      normalizedCount: 0,
      receivedAt,
    })),
    items: [],
    normalizedCount: 0,
    receivedAt,
    expiresAt: '2026-10-20T12:00:30+00:00',
    completedAt: receivedAt,
    attribution: {
      frameUrl: '/rail-fictional-banner.html',
      text: 'Вымышленные данные',
      url: 'https://rasp.yandex.ru/',
      width: 240,
      height: 130,
    },
  };
}
describe('Rail contract', () => {
  it('rejects a different internally generated profile epoch from selected bundle authority', () => {
    const { request, result } = railFixture(now);
    expect(() =>
      decodeRailSearch(result, request, now, {
        profile: 'fictional',
        profileEpoch: 'c'.repeat(32),
        expiresAt: result.expiresAt,
      }),
    ).toThrow();
  });
  it('rejects blank required station names while preserving optional empty labels', () => {
    const fixture = railFixture(now);
    const value = {
      kind: 'complete',
      reasons: [],
      suggestions: [{ reference: fixture.request.from, name: '  ', settlement: '', region: null }],
      hasMore: false,
      catalogueGeneration: fixture.request.catalogueGeneration,
      source: 'yandex-rasp',
      profile: 'fictional',
      profileEpoch: fixture.result.profileEpoch,
      receivedAt: fixture.result.receivedAt,
      expiresAt: fixture.result.expiresAt,
      attribution: fixture.result.attribution,
    };
    expect(() => decodeRailSuggestions(value, now)).toThrow();
  });
  it('rejects duplicate variant facts even when public keys differ', () => {
    const { request, result } = railFixture(now);
    result.items[1] = { ...result.items[0], key: 'forged' };
    result.modes[0].originalCount = 2;
    result.modes[0].normalizedCount = 2;
    result.modes[1].originalCount = 0;
    result.modes[1].normalizedCount = 0;
    expect(() => decodeRailSearch(result, request, now)).toThrow();
  });
  it('rejects a public expiry that extends complete snapshot freshness', () => {
    const { request, result } = railFixture(now);
    result.expiresAt = new Date(now + 301000).toISOString();
    expect(() => decodeRailSearch(result, request, now)).toThrow();
  });
  it('preserves exact price above MAX_SAFE_INTEGER and multiline optional text', () => {
    const { request, result } = railFixture(now);
    result.items[0].carrier = 'Line one\nLine two';
    expect(decodeRailSearch(result, request, now).items[0].tickets.fares[0].price.amount).toBe('9007199254740993.05');
  });
  it.each(['\u007f', '\u0080', '\u009f'])('rejects control optional text %s', (control) => {
    const { request, result } = railFixture(now);
    result.items[0].carrier = `carrier${control}`;
    expect(() => decodeRailSearch(result, request, now)).toThrow();
  });
  it('rejects ticket derived-state forgery without dropping valid marker facts', () => {
    const { request, result } = railFixture(now);
    result.items[0].tickets.damaged = true;
    expect(() => decodeRailSearch(result, request, now)).toThrow();
  });
  it('accepts successful empty only with exact modes/request/current evidence', () => {
    expect(decodeRailSearch(railResult(), railRequest, now).kind).toBe('complete');
  });
  it.each(['modes', 'request', 'count', 'expiry', 'frame'])('rejects inconsistent %s', (field) => {
    const value = railResult();
    if (field === 'modes') value.modes.pop();
    if (field === 'request') value.request = { ...railRequest, transport: 'train' as never };
    if (field === 'count') value.normalizedCount = 1;
    if (field === 'expiry') value.expiresAt = receivedAt;
    if (field === 'frame') value.attribution.frameUrl = 'https://evil.example/';
    expect(() => decodeRailSearch(value, railRequest, now)).toThrow();
  });
  it('unavailable catalogue is distinct from successful no-match', () => {
    const value = {
      kind: 'skipped',
      reasons: ['source-disabled'],
      suggestions: [],
      hasMore: false,
      catalogueGeneration: null,
      source: 'yandex-rasp',
      profile: 'real',
      profileEpoch: 'b'.repeat(32),
      receivedAt: null,
      expiresAt: null,
      attribution: null,
    };
    expect(decodeRailSuggestions(value, now).catalogueGeneration).toBeNull();
  });
});
