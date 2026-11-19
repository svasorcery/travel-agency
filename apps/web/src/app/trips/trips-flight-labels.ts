import type { LinkedBookingStatus } from '@travel/api-client';

export const linkedFlightBookingLabels: Record<LinkedBookingStatus, string> = {
  Held: 'Бронь удерживается',
  Confirmed: 'Подтверждён',
  Ticketed: 'Выписан',
  Cancelled: 'Отменён',
  Refunded: 'Возврат по данным Flights',
};

export const staleFlightObservation = 'Показаны прежние сведения. Текущее состояние неизвестно.';
export const flightObservationRequiresReview = 'Требуется проверка на странице заказа.';
