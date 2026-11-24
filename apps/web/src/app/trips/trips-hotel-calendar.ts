import type { ConfirmedTripHotelLink } from '@travel/api-client';

export function projectTripHotelCalendar(days: readonly { date: string }[], stay: ConfirmedTripHotelLink | null) {
  const entries = new Map<string, string>();
  if (!stay) return entries;
  for (const { date } of days) {
    if (date < stay.checkIn || date > stay.checkOut) continue;
    entries.set(date, date === stay.checkIn ? 'Заезд' : date === stay.checkOut ? 'Выезд' : 'Проживание');
  }
  return entries;
}
