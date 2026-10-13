import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map, timeout } from 'rxjs';
import {
  decodeSupportCase,
  decodeSupportCasePage,
  decodeSupportMessagePage,
  decodeSupportReceipt,
  SupportContractError,
  supportCursor,
  supportGuid,
  supportRevision,
  validSupportText,
} from './support.decoder';
import {
  type CreateSupportCase,
  type SupportActionKind,
  type SupportControl,
  type SupportFilters,
  type SupportMode,
  type SupportReply,
  supportCategories,
  supportPriorities,
  supportStates,
} from './support.types';
import {
  decodeSupportClassificationSuggestion,
  supportClassificationRequestId,
} from './support-classification.decoder';

@Injectable({ providedIn: 'root' })
export class SupportApiService {
  private readonly http = inject(HttpClient);
  private base(mode: SupportMode): string {
    if (mode !== 'customer' && mode !== 'operator') throw new SupportContractError();
    return '/api/support/' + (mode === 'operator' ? 'operator/' : '') + 'cases';
  }
  private headers(token: string | null): HttpHeaders {
    const headers = new HttpHeaders({ 'Content-Type': 'application/json' });
    return token === null ? headers : headers.set('Authorization', 'Bearer ' + token);
  }
  list(mode: SupportMode, filters: SupportFilters, cursor: string | null, token: string | null) {
    let params = new HttpParams();
    if (cursor !== null) params = params.set('cursor', supportCursor(cursor)!);
    for (const [key, values] of [
      ['state', supportStates],
      ['category', supportCategories],
      ['priority', supportPriorities],
    ] as const) {
      const value = filters[key];
      if (value !== undefined) {
        if (!(values as readonly string[]).includes(value)) throw new SupportContractError();
        params = params.set(key, value);
      }
    }
    return this.http
      .get<unknown>(this.base(mode), { headers: this.headers(token), params })
      .pipe(timeout(15_000), map(decodeSupportCasePage));
  }
  detail(mode: SupportMode, id: string, token: string | null) {
    const caseId = supportGuid(id);
    return this.http.get<unknown>(this.base(mode) + '/' + caseId, { headers: this.headers(token) }).pipe(
      timeout(15_000),
      map((value) => decodeSupportCase(value, caseId)),
    );
  }
  messages(mode: SupportMode, id: string, cursor: string | null, token: string | null) {
    const caseId = supportGuid(id);
    const params = cursor === null ? new HttpParams() : new HttpParams().set('cursor', supportCursor(cursor)!);
    return this.http
      .get<unknown>(this.base(mode) + '/' + caseId + '/messages', { headers: this.headers(token), params })
      .pipe(
        timeout(15_000),
        map((value) => decodeSupportMessagePage(value, caseId)),
      );
  }
  receipt(mode: SupportMode, id: string, action: string, kind: SupportActionKind, token: string | null) {
    const caseId = supportGuid(id),
      actionId = supportGuid(action);
    return this.http
      .get<unknown>(this.base(mode) + '/' + caseId + '/actions/' + actionId, { headers: this.headers(token) })
      .pipe(
        timeout(15_000),
        map((value) => decodeSupportReceipt(value, caseId, actionId, kind)),
      );
  }
  create(id: string, body: CreateSupportCase, token: string | null) {
    const caseId = supportGuid(id),
      action = supportGuid(body.messageId);
    if (!validSupportText(body.title, 120) || !validSupportText(body.body, 4000)) throw new SupportContractError();
    const request = {
      messageId: action,
      title: body.title,
      body: body.body,
      ...(body.orderId === undefined ? {} : { orderId: body.orderId === null ? null : supportGuid(body.orderId) }),
    };
    return this.put('customer', caseId, '', action, 'Create', request, token);
  }
  reply(mode: SupportMode, id: string, action: string, body: SupportReply, token: string | null) {
    if (
      !validSupportText(body.body, 4000) ||
      !['Reply', 'ReplyAndReopen'].includes(body.kind) ||
      (mode === 'operator' && body.kind !== 'Reply')
    )
      throw new SupportContractError();
    supportRevision(body.expectedRevision);
    const kind =
      mode === 'operator' ? 'OperatorReply' : body.kind === 'ReplyAndReopen' ? 'ReplyAndReopen' : 'CustomerReply';
    return this.put(mode, id, '/messages/' + supportGuid(action), action, kind, body, token);
  }
  control(id: string, action: string, body: SupportControl, token: string | null) {
    supportRevision(body.expectedRevision);
    if (
      body.kind !== 'Resolve' &&
      (body.kind !== 'SetTriage' ||
        !supportCategories.includes(body.category) ||
        !supportPriorities.includes(body.priority))
    )
      throw new SupportContractError();
    return this.put('operator', id, '/actions/' + supportGuid(action), action, body.kind, body, token);
  }
  classificationSuggestion(id: string, request: string, expectedRevision: number, token: string | null) {
    const caseId = supportGuid(id),
      requestId = supportClassificationRequestId(request);
    supportRevision(expectedRevision);
    return this.http
      .post<unknown>(
        this.base('operator') + '/' + caseId + '/classification-suggestion',
        { requestId, expectedRevision },
        { headers: this.headers(token) },
      )
      .pipe(
        timeout(15_000),
        map((value) => decodeSupportClassificationSuggestion(value, caseId, requestId, expectedRevision)),
      );
  }
  private put(
    mode: SupportMode,
    id: string,
    suffix: string,
    action: string,
    kind: SupportActionKind,
    body: object,
    token: string | null,
  ) {
    const caseId = supportGuid(id),
      actionId = supportGuid(action);
    return this.http.put<unknown>(this.base(mode) + '/' + caseId + suffix, body, { headers: this.headers(token) }).pipe(
      timeout(15_000),
      map((value) => decodeSupportReceipt(value, caseId, actionId, kind)),
    );
  }
}
