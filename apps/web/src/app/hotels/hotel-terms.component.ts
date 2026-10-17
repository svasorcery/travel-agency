import { Component, input } from '@angular/core';
import type { HotelTerms } from '@travel/api-client';

@Component({
  selector: 'app-hotel-terms',
  standalone: true,
  template: `
  @let value = terms();
  <h3>{{ value.property.name }} · {{ value.room }}</h3>
  <p>{{ value.dates.checkIn }} — {{ value.dates.checkOut }} · {{ value.dates.nights }} ночи · {{ value.occupancy.adults }} взрослых</p>
  <p>Вся стоимость проживания: <strong>{{ value.total.amount }} {{ value.total.currency }}</strong></p>
  <p>Налоги, включённые в эту сумму, повторно не прибавляются.</p>
  @if (value.mandatoryLocalPayments === null) {<p>Обязательные платежи на месте неизвестны. Покупка недоступна.</p>}
  @else {@if (value.mandatoryLocalPayments.length === 0) {<p>Дополнительных обязательных платежей на месте нет.</p>}
    @for (amount of value.mandatoryLocalPayments; track $index) {<p>Отдельно на месте: {{ amount.amount }} {{ amount.currency }}</p>}}
  @for (fact of value.board; track $index) {<p>{{ fact.text }} · {{ scope(fact.scope) }}</p>}
  <h4>Отмена и заселение</h4>
  <p>{{ value.cancellation.kind === 'NonRefundable' ? 'Невозвратный тариф' : value.cancellation.kind === 'Unknown' ? 'Правила отмены неизвестны' : 'Условия возвратности тарифа' }}</p>
  @for (rule of value.cancellation.rules; track $index) {<p>{{ rule.text }} · {{ scope(rule.scope) }}</p>}
  @for (condition of value.conditions; track $index) {<p>{{ condition.text }} · {{ scope(condition.scope) }}</p>}
  <p>{{ value.paymentTiming === 'ImmediateTest' ? 'Тестовое подтверждение при бронировании' : 'Этот способ оплаты не поддерживается' }}</p>
`,
})
export class HotelTermsComponent {
  readonly terms = input.required<HotelTerms>();
  scope(value: string): string {
    return (
      (
        {
          WholeStay: 'Всё проживание',
          AllGuests: 'Все гости',
          LeadGuest: 'Основной гость',
          Room: 'На номер',
        } as Record<string, string>
      )[value] ?? value
    );
  }
}
