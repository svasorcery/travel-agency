import { Component, input } from '@angular/core';
import type { HotelOffer, HotelTerms } from '@travel/api-client';

@Component({
  selector: 'app-hotel-terms',
  standalone: true,
  template: `
  @let value = terms();
  <h3>{{ value.property.name }} · {{ value.room }}</h3>
  <p>{{ value.dates.checkIn }} — {{ value.dates.checkOut }} · {{ value.dates.nights }} ночи · {{ value.occupancy.adults }} взрослых</p>
  <p>Сумма поставщику за всё проживание: <strong>{{ value.total.amount }} {{ value.total.currency }}</strong></p>
  <p>Цена продажи путешественнику не определена.</p>
  <p>Налоги, включённые в эту сумму, повторно не прибавляются.</p>
  @if (value.mandatoryLocalPayments === null) {<p>Обязательные платежи на месте неизвестны. Покупка недоступна.</p>}
  @else {@if (value.mandatoryLocalPayments.length === 0) {<p>Дополнительных обязательных платежей на месте нет.</p>}
    @for (amount of value.mandatoryLocalPayments; track $index) {<p>Отдельно на месте: {{ amount.amount }} {{ amount.currency }}</p>}}
  @for (fact of value.board; track $index) {<p>{{ fact.text }} · {{ scope(fact.scope) }}</p>}
  <h4>Отмена и заселение</h4>
  <p>{{ value.cancellation.kind === 'NonRefundable' ? 'Невозвратный тариф' : value.cancellation.kind === 'Unknown' ? 'Правила отмены неизвестны' : 'Условия возвратности тарифа' }}</p>
  @if (conditionEvidence()?.cancellation?.length) {
    @for (rule of conditionEvidence()!.cancellation; track $index) {
      <p>{{ rule.boundary === 'Before' ? 'При отмене до' : 'При отмене начиная с' }} {{ rule.from }}:
      {{ rule.amountKind === 'Refund' ? 'возвратность тарифа' : 'удержание' }} {{ rule.amount.amount }} {{ rule.amount.currency }} · всё проживание. Это не подтверждение выплаты клиенту.</p>
    }
  } @else { @for (rule of value.cancellation.rules; track $index) {<p>{{ rule.text }} · {{ scope(rule.scope) }}</p>} }
  @for (condition of value.conditions; track $index) {<p>{{ condition.text }} · {{ scope(condition.scope) }}</p>}
  <p>{{ value.paymentTiming === 'ImmediateTest' ? 'Тестовое подтверждение при бронировании' : 'Этот способ оплаты не поддерживается' }}</p>
`,
})
export class HotelTermsComponent {
  readonly terms = input.required<HotelTerms>();
  readonly conditionEvidence = input<HotelOffer['conditionEvidence']>(null);
  scope(value: string): string {
    return (
      (
        {
          WholeStay: 'Всё проживание',
          AllGuests: 'Все гости',
          LeadGuest: 'Основной гость',
          Room: 'На номер',
          CoverageUnknown: 'Число гостей и ночей с этим питанием не подтверждено',
          SourceRate: 'Условия выбранного тарифа',
        } as Record<string, string>
      )[value] ?? 'Область действия не уточнена'
    );
  }
}
