import { type TripBlock, tripText } from '@travel/api-client';
import type { RailSelection } from '../rail/rail-state';
export function projectRailIntention(selection: RailSelection): TripBlock {
  if (!selection.isCurrent()) throw new Error('Rail selection changed.');
  const row = selection.journey;
  const noun = row.transport === 'train' ? 'Поезд' : 'Электричка';
  let title = `${noun} (демо)`;
  if (row.number !== null) {
    try {
      title = tripText(`${noun} ${row.number} (демо)`, 100);
    } catch {
      /* Optional source number cannot be rewritten. */
    }
  }
  const note = [
    'Вымышленное демо; историческое намерение, перепроверить.',
    'Источник=yandex-rasp; профиль=fictional.',
    `Вид=${row.transport}; нитка=${row.threadReference}.`,
    `Станции=${row.from.code}→${row.to.code}.`,
    `Отправление=${row.departure}; прибытие=${row.arrival}.`,
    `Получено=${row.receivedAt}; истекает=${selection.expiresAt}.`,
  ].join('\n');
  tripText(note, 500, true);
  return { blockId: crypto.randomUUID(), title, note, startTime: null, endTime: null, estimate: null };
}
