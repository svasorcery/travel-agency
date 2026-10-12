import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import type { FlightFareCondition, FlightFareSummary, FlightFareSummaryAiStatus } from '@travel/api-client';

const AI_FALLBACK_REASONS: Record<FlightFareSummaryAiStatus, string> = {
  Disabled: 'Группировка с AI отключена.',
  NotRequested: 'Группировка с AI не выполнялась.',
  NotAuthenticated: 'Для группировки с AI требуется вход.',
  UnsupportedMode: 'Выбранный способ группировки недоступен.',
  InputUnavailable: 'Для группировки с AI недостаточно проверенных данных.',
  Busy: 'Группировка с AI сейчас занята.',
  ModelUnavailable: 'Группировка с AI сейчас недоступна.',
  InferenceFailed: 'Не удалось выполнить группировку с AI.',
  InvalidOutput: 'Результат группировки с AI не прошёл проверку.',
  UsageUnknown: 'Расход на группировку с AI не удалось подтвердить.',
  AccountingUnavailable: 'Не удалось учесть расход на группировку с AI.',
  DeadlineExceeded: 'Время ожидания группировки с AI истекло.',
  BudgetExceeded: 'Превышен лимит расходов на группировку с AI.',
  SourceStale: 'Предложение изменилось или истекло во время группировки с AI.',
  FreshnessUnknown: 'После группировки с AI не удалось подтвердить актуальность предложения.',
  Ready: 'Показаны структурированные условия.',
};

@Component({
  selector: 'app-flight-fare-summary',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="fare-summary" aria-label="Условия тарифа">
      <h3>{{ historical() ? 'Условия при расчёте предложения' : 'Условия тарифа' }}</h3>
      @if (summary(); as fare) {
        @if (aiNotice(); as message) { <p role="status" data-fare-ai-status>{{ message }}</p> }
        @if (fare.state === 'Unavailable') { <p>Подробные условия не предоставлены.</p> }
        @else {
          @if (stale()) { <p role="status">Сохранённые условия: актуальность не подтверждена. {{ historical() ? 'Это условия при расчёте, а не текущие условия заказа.' : 'Обновите предложение перед продолжением.' }}</p> }
          <p><strong>Все взрослые, всё предложение. Обмен до вылета:</strong> {{ condition(fare.changeBeforeDeparture) }}</p>
          <p><strong>Все взрослые, всё предложение. Возврат до вылета:</strong> {{ condition(fare.refundBeforeDeparture) }}</p>
          <details>
            <summary>Условия по участкам, пассажирам и сегментам</summary>
            @for (leg of fare.legs; track leg.leg) {
              <h4>Участок {{ leg.leg + 1 }}@if (leg.fareBrandName) { · {{ leg.fareBrandName }} }</h4>
              <p>Обмен до вылета на участке: {{ condition(leg.changeBeforeDeparture) }}</p>
              @for (group of groups(); track group.key) {
                @if (group.leg === leg.leg) {
                  <p>Сегмент {{ group.segment + 1 }}, взрослые {{ group.adults }}: код тарифа {{ group.code ?? 'не предоставлен' }}; класс {{ group.cabin ?? 'не предоставлен' }}; зарегистрированный багаж {{ group.checked ?? 'не предоставлен' }}; ручная кладь {{ group.carry ?? 'не предоставлена' }}.</p>
                }
              }
            }
            @for (section of fare.sections; track $index) {
              @for (line of section.lines; track $index) { <p>{{ line.scope }}: {{ line.text }}</p> }
            }
          </details>
        }
        @for (missing of fare.missingInformation; track $index) { <p>{{ missing }}</p> }
        @for (notice of fare.notices; track $index) { <p>{{ notice }}</p> }
      } @else { <p>{{ notice() ?? 'Подробные условия не предоставлены. Старые признаки тарифа не подтверждают правила обмена, возврата или багажа.' }}</p> }
      <p>Указанный сбор не определяет разницу тарифа, сумму возврата или зачисление денег. Полный текст правил, условия после вылета и правила дополнительных услуг могут быть не предоставлены.</p>
    </section>
  `,
  styles: `:host { display: block; } .fare-summary { max-width: 75ch; overflow-wrap: anywhere; } summary { cursor: pointer; padding: .75rem 0; } summary:focus-visible { outline: 2px solid currentColor; outline-offset: 3px; } h4 { margin-block: 1rem .5rem; } p { line-height: 1.5; }`,
})
export class FlightFareSummaryComponent {
  readonly summary = input<FlightFareSummary | null | undefined>(null);
  readonly notice = input<string | undefined>();
  readonly now = input<number>(0);
  readonly historical = input(false);
  readonly organizedRequested = input(false);
  readonly aiNotice = computed(() => {
    const fare = this.summary();
    if (!fare || this.historical()) return null;
    if (fare.presentation === 'AiOrganized')
      return 'Условия сгруппированы с AI. Все факты и суммы взяты из проверенного предложения.';
    if (!this.organizedRequested() && ['Disabled', 'NotRequested', 'Ready'].includes(fare.aiStatus)) return null;
    const reason = AI_FALLBACK_REASONS[fare.aiStatus];
    return fare.aiStatus === 'Ready' ? reason : `${reason} Показаны структурированные условия.`;
  });
  readonly stale = computed(() => {
    const s = this.summary();
    return (
      this.historical() ||
      s?.state !== 'Current' ||
      this.now() >= Math.min(Date.parse(s?.offerExpiresAt ?? ''), Date.parse(s?.firstDepartureAt ?? ''))
    );
  });
  readonly groups = computed(() => {
    const result = new Map<
      string,
      {
        key: string;
        leg: number;
        segment: number;
        adults: string;
        code: string | null;
        cabin: string | null;
        checked: number | null;
        carry: number | null;
      }
    >();
    for (const p of this.summary()?.passengers ?? []) {
      const key = JSON.stringify([
        p.leg,
        p.segment,
        p.fareBasisCode,
        p.cabinClassMarketing,
        p.checkedBaggageQuantity,
        p.carryOnBaggageQuantity,
      ]);
      const previous = result.get(key);
      if (previous) previous.adults += `, ${p.adult + 1}`;
      else
        result.set(key, {
          key,
          leg: p.leg,
          segment: p.segment,
          adults: String(p.adult + 1),
          code: p.fareBasisCode,
          cabin: p.cabinClassMarketing,
          checked: p.checkedBaggageQuantity,
          carry: p.carryOnBaggageQuantity,
        });
    }
    return [...result.values()];
  });
  condition(c: FlightFareCondition | null): string {
    if (c === null) return 'не предоставлено';
    const permission =
      c.permission === 'Allowed'
        ? 'допускается'
        : c.permission === 'NotAllowed'
          ? 'не допускается'
          : 'не предоставлено';
    return `${permission}; сбор ${c.penalty ? `${c.penalty.amount} ${c.penalty.currency}` : 'не предоставлен'}`;
  }
}
