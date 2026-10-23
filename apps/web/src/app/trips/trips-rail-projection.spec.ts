import { railFixture } from '../../../test-fixtures/rail-fixture';
import { projectRailIntention } from './trips-rail-projection';

function selection() {
  const { result } = railFixture();
  return { journey: result.items[0], expiresAt: result.expiresAt!, isCurrent: () => true };
}
describe('Fictional ordinary Rail intention projection', () => {
  it('preserves exact small historical snapshot and leaves time, estimates and large fare outside the write', () => {
    const choice = selection(),
      block = projectRailIntention(choice);
    expect(block.title).toBe('Поезд 001 (демо)');
    expect(block.note).toBe(
      'Вымышленное демо; историческое намерение, перепроверить.\nИсточник=yandex-rasp; профиль=fictional.\nВид=train; нитка=fixture-train.\nСтанции=s1→s2.\nОтправление=2026-10-20T10:00:00+03:00; прибытие=2026-10-20T12:00:00+03:00.\nПолучено=2026-10-20T12:00:00.000Z; истекает=2026-10-20T12:05:00.000Z.',
    );
    expect(block.startTime).toBeNull();
    expect(block.endTime).toBeNull();
    expect(block.estimate).toBeNull();
    expect(block.note).not.toContain('9007199254740993');
  });
  it.each([null, 'x'.repeat(100), 'x\ny', 'x\ty', 'x\ry', '\ud800'])(
    'uses fixed noun fallback for M1-incompatible optional number %s',
    (number) => {
      const choice = selection();
      choice.journey.number = number;
      expect(projectRailIntention(choice).title).toBe('Поезд (демо)');
      choice.journey.transport = 'suburban';
      expect(projectRailIntention(choice).title).toBe('Электричка (демо)');
    },
  );
  it('counts Unicode scalars at the full-title boundary and never normalizes the number', () => {
    const choice = selection();
    choice.journey.number = '🚆'.repeat(87);
    expect([...projectRailIntention(choice).title]).toHaveLength(100);
    expect(projectRailIntention(choice).title).toBe('Поезд ' + choice.journey.number + ' (демо)');
    choice.journey.number += '🚆';
    expect(projectRailIntention(choice).title).toBe('Поезд (демо)');
  });
  it('refuses an overlong or M1-incompatible essential note without truncation', () => {
    const choice = selection();
    choice.journey.threadReference = 'z'.repeat(400);
    expect(() => projectRailIntention(choice)).toThrow();
    choice.journey.threadReference = 'bad\tthread';
    expect(() => projectRailIntention(choice)).toThrow();
  });
});
