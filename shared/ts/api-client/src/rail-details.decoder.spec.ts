import { describe, expect, it } from 'vitest';
import { railDetailsFixture } from '../tests/rail-details-fixture';
import { RailContractError, RailProfileNoticeError } from './rail.decoder';
import { decodeRailDetails } from './rail-details.decoder';

describe('Rail detail contract', () => {
  it('accepts all200 ordered stops and rejects201 without truncation', () => {
    const f = railDetailsFixture();
    const middle = Array.from({ length: 198 }, (_, i) => ({
      ...structuredClone(f.reply.stops[0]),
      sequence: i + 1,
      station: { source: 'yandex-rasp' as const, code: 's3', name: 'Учебная промежуточная' },
      arrival: { state: 'reported' as const, value: '2026-10-20T10:30:00+03:00', reason: null },
      departure: { state: 'reported' as const, value: '2026-10-20T10:30:00+03:00', reason: null },
    }));
    f.reply.stops = [f.reply.stops[0], ...middle, { ...f.reply.stops[1], sequence: 199 }];
    f.reply.alightingIndex = 199;
    expect(decodeRailDetails(f.reply, f.request, f.now, f.authority).stops).toHaveLength(200);
    f.reply.stops.splice(1, 0, structuredClone(middle[0]));
    expect(() => decodeRailDetails(f.reply, f.request, f.now, f.authority)).toThrow(RailContractError);
  });
  it('accepts exact section with independent terminal and zero dwell facts', () => {
    const f = railDetailsFixture();
    expect(decodeRailDetails(f.reply, f.request, f.now, f.authority)).toEqual(f.reply);
  });
  it.each([
    'endpoint',
    'offset',
    'sequence',
    'indices',
    'verification',
    'verification-mode',
    'verification-later',
    'expiry',
    'expired',
    'fractional',
    'chronology',
    'dwell',
    'state',
    'complete-defect',
    'limited-without-defect',
    'stops',
    'request',
  ])('rejects impossible %s without partially applying data', (defect) => {
    const f = railDetailsFixture();
    const r = f.reply;
    switch (defect) {
      case 'endpoint':
        r.stops[0].station.code = 's9';
        break;
      case 'offset':
        r.stops[0].departure.value = '2026-10-20T07:00:00Z';
        break;
      case 'sequence':
        r.stops[1].sequence = 7;
        break;
      case 'indices':
        r.boardingIndex = 1;
        break;
      case 'verification':
        r.verification = null;
        break;
      case 'verification-mode':
        r.verification!.transport = 'suburban';
        break;
      case 'verification-later':
        r.verification!.receivedAt = new Date(f.now + 1).toISOString();
        break;
      case 'expiry':
        r.expiresAt = new Date(f.now + 60001).toISOString();
        break;
      case 'expired':
        r.expiresAt = new Date(f.now).toISOString();
        break;
      case 'fractional':
        r.stops[0].departure.value = '2026-10-20T10:00:00.1+03:00';
        break;
      case 'chronology':
        r.stops[1].departure = { state: 'reported', value: '2026-10-20T06:00:00Z', reason: null };
        break;
      case 'dwell':
        r.stops[1].dwellSeconds.value = 9007199254740992;
        break;
      case 'state':
        r.stops[0].platform = { state: 'reported', value: null, reason: 'missing-platform' };
        break;
      case 'complete-defect':
        r.reasons = ['invalid-time'];
        break;
      case 'limited-without-defect':
        r.kind = 'limited';
        break;
      case 'stops':
        r.stops = [];
        break;
      case 'request':
        r.request = { ...r.request, selection: { ...r.request.selection, key: 'other' } };
        break;
    }
    expect(() => decodeRailDetails(r, f.request, f.now, f.authority)).toThrow(RailContractError);
  });
  it('profile mismatch clears via notice before malformed old details are inspected', () => {
    const f = railDetailsFixture();
    expect(() =>
      decodeRailDetails(
        { source: 'yandex-rasp', profile: 'real', profileEpoch: 'c'.repeat(32), stops: 'broken' },
        f.request,
        f.now,
        f.authority,
      ),
    ).toThrow(RailProfileNoticeError);
  });
  it('accepts failed route only without usable facts', () => {
    const f = railDetailsFixture();
    const reply = {
      ...f.reply,
      kind: 'failed',
      reasons: ['supplier-http'],
      stops: [],
      boardingIndex: null,
      alightingIndex: null,
      expiresAt: null,
      attribution: null,
    };
    expect(decodeRailDetails(reply, f.request, f.now, f.authority).kind).toBe('failed');
    expect(() => decodeRailDetails({ ...reply, stage: 'admission' }, f.request, f.now, f.authority)).toThrow(
      RailContractError,
    );
  });
  it('rejects usable current-authority reply bound to an old requested epoch', () => {
    const f = railDetailsFixture();
    f.reply.request.expectedProfileEpoch = 'a'.repeat(32);
    expect(() => decodeRailDetails(f.reply, f.reply.request, f.now, f.authority)).toThrow(RailContractError);
  });
  it('accepts limited field/metadata outcomes and typed admission/verification skips', () => {
    const f = railDetailsFixture();
    const limited = structuredClone(f.reply);
    limited.kind = 'limited';
    limited.reasons = ['echo-date-unavailable'];
    expect(decodeRailDetails(limited, f.request, f.now, f.authority).kind).toBe('limited');
    limited.reasons = ['invalid-platform'];
    limited.stops[0].platform = { state: 'unavailable', value: null, reason: 'invalid-platform' };
    limited.stops[0].reasons = ['invalid-platform'];
    expect(decodeRailDetails(limited, f.request, f.now, f.authority).stops[0].departure.state).toBe('reported');
    const empty = {
      ...f.reply,
      stops: [],
      boardingIndex: null,
      alightingIndex: null,
      expiresAt: null,
      routeReceivedAt: null,
      attribution: null,
    };
    expect(
      decodeRailDetails(
        { ...empty, kind: 'skipped', stage: 'admission', reasons: ['details-disabled'], verification: null },
        f.request,
        f.now,
        f.authority,
      ).stage,
    ).toBe('admission');
    for (const reason of ['selection-not-confirmed', 'route-locator-unavailable'])
      expect(
        decodeRailDetails(
          { ...empty, kind: 'skipped', stage: 'verification', reasons: [reason] },
          f.request,
          f.now,
          f.authority,
        ).kind,
      ).toBe('skipped');
    expect(
      decodeRailDetails(
        {
          ...empty,
          kind: 'skipped',
          stage: 'verification',
          reasons: ['quota'],
          verification: { transport: 'train', kind: 'skipped', reasons: ['quota'], receivedAt: null },
        },
        f.request,
        f.now,
        f.authority,
      ).kind,
    ).toBe('skipped');
    expect(() =>
      decodeRailDetails(
        { ...empty, kind: 'skipped', stage: 'admission', reasons: ['profile-changed'], verification: null },
        f.request,
        f.now,
        f.authority,
      ),
    ).toThrow(RailProfileNoticeError);
  });
});
