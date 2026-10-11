import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import type { CustomerRefundState } from '@travel/api-client';
import { FlightCustomerRefundService } from './flight-customer-refund.service';

@Component({
  selector: 'app-flight-customer-refund',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './flight-customer-refund.component.html',
  styleUrl: './flight-customer-refund.component.scss',
})
export class FlightCustomerRefundComponent {
  readonly view = inject(FlightCustomerRefundService);
  caption(state: CustomerRefundState): string {
    const captions: Record<CustomerRefundState, string> = {
      NotApplicable: 'Возврат клиенту ещё не требуется',
      Assessing: 'Проверяем основание возврата',
      Ready: 'Демо-возврат подготовлен',
      DispatchClaimed: 'Отправка ещё проверяется',
      Pending: 'Отправка ещё проверяется',
      Unknown: 'Результат отправки неизвестен',
      ManualReviewRequired: 'Требуется ручная проверка',
      Sent: 'Демо: возврат отправлен',
      ConfirmedNoEffect: 'Подтверждено: выплата не выполнена',
      NoRefundDue: 'По условиям отмены сумма возврата — 0',
      NoCustomerPayment: 'Подтверждено: клиентский платёж не выполнялся',
      LegacyUnverified: 'Историческая запись не доказывает выплату клиенту',
    };
    return captions[state];
  }
}
