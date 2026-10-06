import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  effect,
  Injector,
  inject,
} from '@angular/core';
import type { CancellationSource } from '@travel/api-client';
import { FlightCancellationService } from './flight-cancellation.service';
import { formatCabinClass, formatOffsetTime, groundGapNote } from './flight-results';

@Component({
  selector: 'app-flight-cancellation-review',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './flight-cancellation-review.component.html',
  styleUrl: './flight-cancellation-review.component.scss',
})
export class FlightCancellationReviewComponent {
  readonly view = inject(FlightCancellationService);
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private focusedTerms = '';
  constructor() {
    effect(() => {
      const operation = this.view.snapshot()?.operation;
      const key = operation?.phase === 'TermsReady' ? (operation.terms?.hash ?? '') : '';
      if (key === '' || key === this.focusedTerms) return;
      this.focusedTerms = key;
      afterNextRender(
        () => {
          if (this.view.snapshot()?.operation?.terms?.hash === key)
            this.element.nativeElement.querySelector<HTMLElement>('#cancellation-terms-heading')?.focus();
        },
        { injector: this.injector },
      );
    });
  }
  readonly formatOffsetTime = formatOffsetTime;
  readonly formatCabinClass = formatCabinClass;
  readonly groundGapNote = groundGapNote;
  accept(event: Event): void {
    if (event.target instanceof HTMLInputElement) this.view.accepted.set(event.target.checked);
  }
  rejectionNotice(source: CancellationSource): string {
    switch (source) {
      case 'SupplierApi':
        return 'Поставщик подтвердил отказ в отмене.';
      case 'OperatorVerified':
        return 'Оператор подтвердил отсутствие исполнения отмены.';
      case 'TravelAdmission':
        return 'Запрос отмены не отправлен.';
      default:
        return 'Источник результата требует проверки.';
    }
  }
  destination(value: string): string {
    const labels: Readonly<Record<string, string>> = {
      Balance: 'Баланс агентства у поставщика',
      Card: 'Карта агентства',
      ArcBspCash: 'Расчёты ARC/BSP',
      AwaitingPayment: 'Неоплаченный заказ',
      OriginalFormOfPayment: 'Исходный способ оплаты агентства',
    };
    return labels[value] ?? 'Требуется проверка способа возврата';
  }
}
