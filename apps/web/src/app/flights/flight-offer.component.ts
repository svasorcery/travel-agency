import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import type { FlightOfferView } from './flight-results';

@Component({
  selector: 'app-flight-offer',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './flight-offer.component.scss',
  template: `
    @if (offer(); as item) {
      <article class="offer" [attr.aria-label]="'Предложение ' + item.provider">
        <div class="offer__top">
          <div>
            <span class="offer__eyebrow">{{ item.kind === 'bookable' ? 'Маршрут' : 'Партнёрское предложение' }}</span>
            <h3>{{ item.kind === 'bookable' ? item.journeyLabel : item.partnerName }}</h3>
            <p class="offer__source">Источник: {{ item.provider }}</p>
          </div>
          <div class="offer__price">
            <strong>{{ item.price }}</strong>
            <span>{{ priceLabel() }}{{ item.kind === 'bookable' ? ' · за группу (' + item.passengerCount + ')' : '' }}</span>
          </div>
        </div>

        @if (item.ranking; as ranking) {
          <details class="offer__ranking">
            <summary>Почему здесь: №{{ ranking.rank }} в {{ ranking.currency }}</summary>
            <p>{{ ranking.priceNote }}</p>
            <p>{{ ranking.duration }} · {{ ranking.transfers }}</p>
            <p>Сначала цена; при равной цене сравниваем длительность, затем пересадки.
              Известные данные идут перед неизвестными. Это не означает, что неизвестный маршрут хуже.</p>
            <p>Полное совпадение факторов разрешается стабильным порядком, без предпочтения качества.
              Цены предварительные, условия требуют проверки.</p>
          </details>
        }
        @if (item.kind === 'bookable') {
          <p class="offer__foot">Цена за всю группу и все участки. Длительность перелётов: {{ item.totalDuration }}, включая пересадки внутри участков; ожидание между участками и наземный путь не включены. Время каждого аэропорта указано с его UTC смещением.</p>
          <div class="offer__slices">
            @for (slice of item.slices; track $index) {
              <div class="offer__slice">
                @if (slice.groundGap) { <p role="note">{{ slice.groundGap }}</p> }
                <p>Участок {{ $index + 1 }}</p>
                <div class="offer__route">
                  <span>{{ slice.origin }}</span>
                  <span class="offer__line" aria-hidden="true"></span>
                  <span>{{ slice.destination }}</span>
                </div>
                <div class="offer__time"><span>{{ slice.departure }}</span><span>{{ slice.arrival }}</span></div>
                @if (slice.transferCount === 0) {
                  <p>{{ slice.segments[0].flight }} · {{ slice.segments[0].cabinClass }} · {{ slice.duration }} · Без пересадок</p>
                } @else {
                  <ol class="offer__legs" aria-label="Сегменты перелёта">
                    @for (segment of slice.segments; track $index) {
                      <li>
                        <strong>{{ segment.flight }}</strong>
                        <span>{{ segment.cabinClass }}</span>
                        <span>{{ segment.route }}</span>
                        <span>{{ segment.departure }} → {{ segment.arrival }}</span>
                      </li>
                    }
                  </ol>
                  <p>{{ slice.duration }} · Пересадок: {{ slice.transferCount }}</p>
                }
              </div>
            }
          </div>
          @if (showQuoteAction()) {
            <div class="offer__action">
              <button type="button" data-action="quote" [disabled]="quoteBusy() || !item.holdEligible" (click)="quoteRequested.emit(item.id)">
                Проверить цену <span aria-hidden="true">→</span>
              </button>
              @if (!item.holdEligible) { <p>Удержание недоступно: {{ item.holdIneligibilityReason === 'identity-documents-required' ? 'нужны документы, пока не поддерживаемые при оформлении' : item.holdIneligibilityReason === 'hold-not-supported' ? 'поставщик не поддерживает удержание' : 'возможность удержания не подтверждена' }}.</p> } @else { <p>Доступность и цена уточняются перед оформлением.</p> }
            </div>
          }
        } @else {
          <p class="offer__partner-route">{{ item.direction }}</p>
          <p class="offer__foot">Детали маршрута уточняются у партнёра. Переход к партнёру пока недоступен.</p>
        }
      </article>
    }
  `,
})
export class FlightOfferComponent {
  readonly offer = input.required<FlightOfferView>();
  readonly showQuoteAction = input(true);
  readonly priceLabel = input('Предварительная цена');
  readonly quoteBusy = input(false);
  readonly quoteRequested = output<string>();
}
