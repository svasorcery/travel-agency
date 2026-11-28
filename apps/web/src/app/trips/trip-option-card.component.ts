import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { type TripBrief, type TripCurrency, type TripOptionPreview, tripDecimal, tripMinor } from '@travel/api-client';

const labels = {
  directionUnsupported: 'Пример доступен для поездки из Москвы в Санкт-Петербург и обратно.',
  dateRangeUnsupported: 'Для примера выберите два или три календарных дня с одной или двумя ночами.',
  groupUnsupported: 'В одной комнате примера могут разместиться один или два взрослых.',
  horizonUnsupported: 'Выбранные даты выходят за период подбора исторического примера.',
  flightUnavailable: 'На эти даты и состав группы нет перелёта туда и обратно.',
  hotelUnavailable: 'В гостиницах нет номера для всей группы на все ночи поездки.',
  timeIncompatible: 'Время перелёта несовместимо с заездом или выездом из гостиницы.',
  budgetExceeded: 'Известные обязательные расходы превышают указанный бюджет.',
  noWalks: 'Ни в один из дней не удалось вместить прогулку.',
  onlyCompatibleOption: 'Только это сочетание перелёта и проживания подошло по заданным условиям.',
  moreInterests: 'Программа охватывает больше выбранных интересов.',
  moreStops: 'В заданном темпе можно посетить больше мест.',
  completeFlightAndStay: 'При равной программе известна полная цена перелёта и проживания.',
  lowerComparablePrice: 'При равной программе и полноте цены это сочетание стоит меньше.',
  equalByStableIds: 'Варианты равноценны. Для воспроизводимости выбран первый по идентификаторам предложений.',
  noWalkingWindow: 'После перелёта, заезда или выезда и дороги времени на прогулку не остаётся.',
  noEligibleStops: 'В доступный интервал не помещается подходящая прогулка.',
  architecture: 'архитектура',
  culture: 'культура',
  nature: 'природа',
  history: 'история',
  food: 'еда',
  flightBase: 'Перелёт туда и обратно',
  hotelBase: 'Проживание за все ночи',
  includedTax: 'Включённые налоги и сборы',
  localFee: 'Местный сбор',
  meals: 'питание',
  transfers: 'трансферы',
};
@Component({
  selector: 'app-trip-option-card',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './trip-option-card.component.html',
  styleUrl: './trip-option-card.component.scss',
})
export class TripOptionCardComponent {
  readonly result = input.required<TripOptionPreview>();
  readonly brief = input.required<TripBrief>();
  readonly historical = input(false);
  readonly labels = labels;
  readonly costs = computed(() => {
    const price = this.result().option?.price;
    if (!price) return null;
    const amounts = new Map<TripCurrency, bigint>();
    const knownComponents = new Set<string>();
    let unknownMandatory = false;
    for (const line of price.components) {
      if (line.kind === 'unknownMandatory') unknownMandatory = true;
      if (line.kind !== 'travelerMandatory') continue;
      if (line.currency === null || line.amount === null) {
        unknownMandatory = true;
        continue;
      }
      knownComponents.add(line.component);
      amounts.set(line.currency, (amounts.get(line.currency) ?? 0n) + tripMinor(line.amount));
    }
    return {
      totals: Array.from(amounts, ([currency, minor]) => ({ currency, amount: tripDecimal(minor) })),
      foreignCurrency: Array.from(amounts).some(([currency, minor]) => currency !== price.currency && minor !== 0n),
      knownComponents,
      incomplete: unknownMandatory || !knownComponents.has('flightBase') || !knownComponents.has('hotelBase'),
      budget: this.brief().totalBudget,
    };
  });
  localTime(iso: string): string {
    const local = `${iso.slice(0, 10)} ${iso.slice(11, 16)}`;
    if (!this.historical()) return local;
    const offset = iso.endsWith('Z') ? '' : iso.slice(-6);
    return `${local} UTC${offset}`;
  }
  amount(amount: string, currency: string): string {
    return new Intl.NumberFormat('ru-RU', {
      style: 'currency',
      currency,
      minimumFractionDigits: Number.isInteger(Number(amount)) ? 0 : 2,
    }).format(Number(amount));
  }
}
