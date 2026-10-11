import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map, type Observable, timeout } from 'rxjs';
import { cancellationGuid } from './flights-cancellation.decoder';
import { decodeCustomerRefundStatus } from './flights-customer-refund.decoder';
import type { CustomerRefundStatus } from './flights-customer-refund.types';

@Injectable({ providedIn: 'root' })
export class FlightsCustomerRefundApiService {
  private readonly http = inject(HttpClient);
  status(aggregateId: string, token: string | null): Observable<CustomerRefundStatus> {
    const id = cancellationGuid(aggregateId);
    const headers = token === null ? new HttpHeaders() : new HttpHeaders({ Authorization: 'Bearer ' + token });
    return this.http.get<unknown>('/api/flights/orders/' + id + '/customer-refund', { headers }).pipe(
      timeout(15_000),
      map((value) => decodeCustomerRefundStatus(value, id)),
    );
  }
}
