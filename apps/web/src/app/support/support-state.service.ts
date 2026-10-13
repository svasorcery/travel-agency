import { HttpErrorResponse } from '@angular/common/http';
import { computed, effect, Injectable, inject, signal, untracked } from '@angular/core';
import {
  type SupportActionKind,
  SupportApiService,
  type SupportCase,
  type SupportCategory,
  SupportContractError,
  type SupportFilters,
  type SupportMessage,
  type SupportMode,
  type SupportPriority,
  type SupportReceipt,
  supportGuid,
  validSupportText,
} from '@travel/api-client';
import { firstValueFrom, type Observable } from 'rxjs';
import { SupportAuthService } from './support-auth.service';

export interface SupportUnresolvedAction {
  caseId: string;
  actionId: string;
  kind: SupportActionKind;
  state: 'pending' | 'unknown';
}
interface ActionBarrier extends SupportUnresolvedAction {
  owner: string;
  mode: SupportMode;
}
interface Context {
  owner: string;
  mode: SupportMode;
  epoch: number;
  generation: number;
}
/** Private text lives only in this tab. Barriers contain resource metadata, never request bodies. */
@Injectable({ providedIn: 'root' })
export class SupportStateService {
  readonly auth = inject(SupportAuthService);
  private readonly api = inject(SupportApiService);
  readonly mode = signal<SupportMode>('customer');
  readonly caseId = signal<string | null>(null);
  readonly cases = signal<SupportCase[]>([]);
  readonly selected = signal<SupportCase | null>(null);
  readonly messages = signal<SupportMessage[]>([]);
  readonly nextCases = signal<string | null>(null);
  readonly nextMessages = signal<string | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly notice = signal<string | null>(null);
  readonly reviewRequired = signal(false);
  readonly orderLinkRejected = signal(false);
  readonly privateGeneration = signal(0);
  private readonly barriers = signal<ActionBarrier[]>([]);
  private readonly filters = signal<SupportFilters>({});
  private contextKey = '';
  private readonly boundIdentity = signal<string | null>(null);
  private routeVersion = 0;
  private readVersion = 0;
  private messagesVersion = 0;
  readonly authorized = computed(
    () =>
      this.auth.status().kind === 'authenticated' &&
      this.auth.mode() === this.mode() &&
      this.boundIdentity() === this.identityKey(),
  );
  readonly unresolved = computed<SupportUnresolvedAction[]>(() => {
    const status = this.auth.status();
    if (status.kind !== 'authenticated') return [];
    return this.barriers()
      .filter(
        (action) =>
          action.owner === status.userId.toLowerCase() &&
          action.mode === this.mode() &&
          (this.caseId() === null || action.caseId === this.caseId()),
      )
      .map(({ caseId, actionId, kind, state }) => ({ caseId, actionId, kind, state }));
  });
  readonly writeBlocked = computed(
    () => !this.authorized() || this.loading() || this.reviewRequired() || this.unresolved().length > 0,
  );
  constructor() {
    effect(() => {
      const status = this.auth.status(),
        epoch = this.auth.identityEpoch(),
        mode = this.auth.mode();
      const key =
        mode + ':' + epoch + ':' + (status.kind === 'authenticated' ? status.userId.toLowerCase() : status.kind);
      untracked(() => {
        if (this.contextKey && this.contextKey !== key) this.erasePrivate();
        this.contextKey = key;
      });
    });
  }
  async enter(mode: SupportMode, id: string | null): Promise<void> {
    const routeVersion = ++this.routeVersion;
    this.auth.selectMode(mode);
    this.erasePrivate();
    this.mode.set(mode);
    try {
      this.caseId.set(id === null ? null : supportGuid(id));
    } catch {
      this.caseId.set(null);
      this.error.set('Неверная ссылка на обращение.');
      return;
    }
    if (this.auth.hasOidcCallback()) await this.auth.initializeFromCallback();
    if (routeVersion !== this.routeVersion || mode !== this.mode()) return;
    this.contextKey = this.identityKey();
    this.boundIdentity.set(this.contextKey);
    if (this.authorized()) await this.refresh();
  }
  leave(): void {
    this.routeVersion++;
    this.erasePrivate();
    this.contextKey = '';
  }
  private identityKey(): string {
    const status = this.auth.status();
    return (
      this.auth.mode() +
      ':' +
      this.auth.identityEpoch() +
      ':' +
      (status.kind === 'authenticated' ? status.userId.toLowerCase() : status.kind)
    );
  }
  private capture(): Context | null {
    const status = this.auth.status();
    if (status.kind !== 'authenticated' || !this.authorized()) return null;
    return {
      owner: status.userId.toLowerCase(),
      mode: this.mode(),
      epoch: this.auth.identityEpoch(),
      generation: this.privateGeneration(),
    };
  }
  private current(context: Context): boolean {
    const status = this.auth.status();
    return (
      context.generation === this.privateGeneration() &&
      context.epoch === this.auth.identityEpoch() &&
      context.mode === this.mode() &&
      context.mode === this.auth.mode() &&
      status.kind === 'authenticated' &&
      context.owner === status.userId.toLowerCase()
    );
  }
  private erasePrivate(): void {
    this.boundIdentity.set(null);
    this.privateGeneration.update((v) => v + 1);
    this.readVersion++;
    this.messagesVersion++;
    this.cases.set([]);
    this.selected.set(null);
    this.messages.set([]);
    this.nextCases.set(null);
    this.nextMessages.set(null);
    this.loading.set(false);
    this.error.set(null);
    this.notice.set(null);
    this.reviewRequired.set(false);
    this.orderLinkRejected.set(false);
    this.barriers.update((rows) => rows.map((row) => (row.state === 'pending' ? { ...row, state: 'unknown' } : row)));
  }
  private deny(context: Context): void {
    if (!this.current(context)) return;
    this.erasePrivate();
    this.auth.deny(context.epoch);
  }
  private readError(error: unknown, context: Context): void {
    if (!this.current(context)) return;
    if (error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403)) {
      this.deny(context);
      return;
    }
    if (error instanceof HttpErrorResponse && error.status === 404 && this.caseId() !== null) {
      this.selected.set(null);
      this.messages.set([]);
      this.nextMessages.set(null);
    }
    this.error.set(
      error instanceof SupportContractError || (error instanceof HttpErrorResponse && error.status === 200)
        ? 'Ответ поддержки не удалось прочитать. Обновите данные вручную.'
        : error instanceof HttpErrorResponse && error.status === 404
          ? 'Обращение не найдено или больше недоступно. Это не подтверждает исход отправленного действия.'
          : 'Не удалось получить данные поддержки. Обновите их вручную.',
    );
  }
  async refresh(filters?: SupportFilters): Promise<void> {
    const context = this.capture();
    if (!context) return;
    if (filters !== undefined) this.filters.set({ ...filters });
    const version = ++this.readVersion;
    this.messagesVersion++;
    const id = this.caseId();
    this.loading.set(true);
    this.error.set(null);
    try {
      const token = await this.auth.accessToken();
      if (!this.current(context) || version !== this.readVersion) return;
      if (id === null) {
        const page = await firstValueFrom(this.api.list(context.mode, this.filters(), null, token));
        if (!this.current(context) || version !== this.readVersion) return;
        this.validateOwners(page.items, context);
        this.cases.set(page.items);
        this.nextCases.set(page.nextCursor);
      } else {
        const observeRead = <T>(request: Observable<T>): Promise<T> =>
          firstValueFrom(request).catch((error) => {
            // Each current authorization denial matters, even if its parallel read failed first.
            if (
              version === this.readVersion &&
              error instanceof HttpErrorResponse &&
              (error.status === 401 || error.status === 403)
            )
              this.readError(error, context);
            throw error;
          });
        const [row, page] = await Promise.all([
          observeRead(this.api.detail(context.mode, id, token)),
          observeRead(this.api.messages(context.mode, id, null, token)),
        ]);
        if (!this.current(context) || version !== this.readVersion) return;
        this.validateOwners([row], context);
        const previous = this.selected();
        if (previous === null || row.revision >= previous.revision) this.selected.set(row);
        this.messages.set(page.items);
        this.nextMessages.set(page.nextCursor);
      }
      this.reviewRequired.set(false);
    } catch (error) {
      if (version === this.readVersion) this.readError(error, context);
    } finally {
      if (this.current(context) && version === this.readVersion) this.loading.set(false);
    }
  }
  async moreCases(): Promise<void> {
    const context = this.capture(),
      cursor = this.nextCases();
    if (!context || !cursor || this.loading()) return;
    const version = ++this.readVersion;
    this.loading.set(true);
    this.error.set(null);
    try {
      const token = await this.auth.accessToken();
      if (!this.current(context) || version !== this.readVersion) return;
      const page = await firstValueFrom(this.api.list(context.mode, this.filters(), cursor, token));
      if (!this.current(context) || version !== this.readVersion) return;
      this.validateOwners(page.items, context);
      this.cases.set(this.merge(this.cases(), page.items, (item) => item.caseId));
      this.nextCases.set(page.nextCursor);
    } catch (error) {
      if (version === this.readVersion) this.readError(error, context);
    } finally {
      if (this.current(context) && version === this.readVersion) this.loading.set(false);
    }
  }
  async moreMessages(): Promise<void> {
    const context = this.capture(),
      cursor = this.nextMessages(),
      id = this.caseId();
    if (!context || !cursor || !id || this.loading()) return;
    const version = ++this.messagesVersion;
    this.loading.set(true);
    this.error.set(null);
    try {
      const token = await this.auth.accessToken();
      if (!this.current(context) || version !== this.messagesVersion) return;
      const page = await firstValueFrom(this.api.messages(context.mode, id, cursor, token));
      if (!this.current(context) || version !== this.messagesVersion) return;
      this.messages.set(this.merge(this.messages(), page.items, (item) => item.messageId));
      this.nextMessages.set(page.nextCursor);
    } catch (error) {
      if (version === this.messagesVersion) this.readError(error, context);
    } finally {
      if (this.current(context) && version === this.messagesVersion) this.loading.set(false);
    }
  }
  private validateOwners(rows: SupportCase[], context: Context): void {
    if (context.mode === 'customer' && rows.some((row) => row.ownerId.toLowerCase() !== context.owner))
      throw new SupportContractError();
  }
  private merge<T>(before: T[], after: T[], key: (row: T) => string): T[] {
    const result = new Map(before.map((row) => [key(row), row]));
    for (const row of after) result.set(key(row), row);
    return [...result.values()];
  }
  async create(title: string, body: string, orderId: string | null): Promise<string | null> {
    if (this.mode() !== 'customer' || this.writeBlocked() || this.orderLinkRejected()) return null;
    if (!validSupportText(title, 120) || !validSupportText(body, 4000)) {
      this.error.set('Укажите тему до 120 символов и сообщение до 4000 символов.');
      return null;
    }
    try {
      if (orderId !== null) supportGuid(orderId);
    } catch {
      this.error.set('Проверьте ссылку на заказ.');
      return null;
    }
    const id = crypto.randomUUID(),
      action = crypto.randomUUID();
    const applied = await this.mutate(id, action, 'Create', (token) =>
      this.api.create(id, { messageId: action, title, body, orderId }, token),
    );
    return applied ? id : null;
  }
  async createWithoutOrder(title: string, body: string): Promise<string | null> {
    if (!this.orderLinkRejected() || this.mode() !== 'customer' || this.writeBlocked()) return null;
    this.orderLinkRejected.set(false);
    return this.create(title, body, null);
  }
  async reply(body: string, reopen: boolean): Promise<boolean> {
    const row = this.selected();
    if (!row || this.writeBlocked()) return false;
    if (!validSupportText(body, 4000)) {
      this.error.set('Введите сообщение до 4000 символов.');
      return false;
    }
    if (
      row.state === 'Resolved' &&
      (!reopen || this.mode() !== 'customer' || !row.purgeAt || Date.parse(row.purgeAt) <= Date.now())
    )
      return false;
    if (row.state !== 'Resolved' && reopen) return false;
    const kind = this.mode() === 'operator' ? 'OperatorReply' : reopen ? 'ReplyAndReopen' : 'CustomerReply',
      action = crypto.randomUUID();
    return this.mutate(row.caseId, action, kind, (token) =>
      this.api.reply(
        this.mode(),
        row.caseId,
        action,
        { body, expectedRevision: row.revision, kind: reopen ? 'ReplyAndReopen' : 'Reply' },
        token,
      ),
    );
  }
  async resolve(): Promise<boolean> {
    const row = this.selected();
    if (!row || row.state === 'Resolved' || this.mode() !== 'operator' || this.writeBlocked()) return false;
    const action = crypto.randomUUID();
    return this.mutate(row.caseId, action, 'Resolve', (token) =>
      this.api.control(row.caseId, action, { kind: 'Resolve', expectedRevision: row.revision }, token),
    );
  }
  async triage(category: SupportCategory, priority: SupportPriority): Promise<boolean> {
    const row = this.selected();
    if (!row || this.mode() !== 'operator' || this.writeBlocked()) return false;
    const action = crypto.randomUUID();
    return this.mutate(row.caseId, action, 'SetTriage', (token) =>
      this.api.control(
        row.caseId,
        action,
        { kind: 'SetTriage', expectedRevision: row.revision, category, priority },
        token,
      ),
    );
  }
  private async mutate(
    id: string,
    actionId: string,
    kind: SupportActionKind,
    request: (token: string | null) => Observable<SupportReceipt>,
  ): Promise<boolean> {
    const context = this.capture();
    if (!context) return false;
    let sent = false;
    this.error.set(null);
    this.notice.set(null);
    try {
      const token = await this.auth.accessToken();
      if (!this.current(context) || this.writeBlocked()) return false;
      // Validation happens before dispatch. Only this one subscription may send the mutation.
      const operation = request(token);
      this.barriers.update((rows) => [
        ...rows,
        { caseId: id, actionId, kind, state: 'pending', owner: context.owner, mode: context.mode },
      ]);
      sent = true;
      const receipt = await firstValueFrom(operation);
      if (!this.current(context)) return false;
      this.acknowledge(receipt, context);
      this.notice.set('Действие сохранено. Текущее состояние читается отдельно.');
      if (kind !== 'Create') await this.refresh();
      return true;
    } catch (error) {
      if (!this.current(context)) return false;
      if (sent) {
        const fixed =
          error instanceof HttpErrorResponse &&
          typeof error.error?.type === 'string' &&
          /(?:^|\/)Support\.[A-Za-z.]+$/.test(error.error.type);
        const orderLinkFailure =
          kind === 'Create' &&
          error instanceof HttpErrorResponse &&
          ((error.status === 404 && error.error?.type === 'https://travel.local/errors/Support.OrderLinkNotFound') ||
            (error.status === 503 &&
              error.error?.type === 'https://travel.local/errors/Support.OrderLinkUnavailable') ||
            (error.status === 404 && error.error?.type === 'Support.OrderLinkNotFound') ||
            (error.status === 503 && error.error?.type === 'Support.OrderLinkUnavailable'));
        if (orderLinkFailure) {
          // These two reserved responses are guaranteed to precede protection/store/commit.
          this.removeBarrier(context, id, actionId);
          this.orderLinkRejected.set(true);
          this.error.set(
            'Связь с заказом не проверена. Черновик сохранён; обращение не создано. Можно явно отправить новое обращение без связи с заказом.',
          );
        } else if (fixed && error instanceof HttpErrorResponse && [400, 409, 413].includes(error.status)) {
          this.removeBarrier(context, id, actionId);
          this.reviewRequired.set(error.status === 409);
          this.error.set(
            error.status === 409
              ? 'Обращение изменилось. Обновите состояние и заново выберите действие.'
              : 'Проверьте тему, сообщение и связь с заказом. Запрос не принят.',
          );
        } else {
          this.barriers.update((rows) =>
            rows.map((row) =>
              row.owner === context.owner && row.mode === context.mode && row.caseId === id && row.actionId === actionId
                ? { ...row, state: 'unknown' }
                : row,
            ),
          );
          this.error.set(
            'Ответ на действие не получен. Исход неизвестен; повторная отправка заблокирована. Проверьте сохранённое действие.',
          );
        }
      } else this.error.set('Не удалось подготовить отправку. Войдите снова или проверьте поля.');
      if (error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403)) this.deny(context);
      return false;
    }
  }
  private removeBarrier(context: Context, id: string, actionId: string): void {
    this.barriers.update((rows) =>
      rows.filter(
        (row) =>
          !(row.owner === context.owner && row.mode === context.mode && row.caseId === id && row.actionId === actionId),
      ),
    );
  }
  private acknowledge(receipt: SupportReceipt, context: Context): void {
    this.removeBarrier(context, receipt.caseId, receipt.actionId);
  }
  async reconcile(action: SupportUnresolvedAction): Promise<void> {
    const context = this.capture();
    if (
      !context ||
      !this.unresolved().some((row) => row.caseId === action.caseId && row.actionId === action.actionId) ||
      action.state === 'pending'
    )
      return;
    try {
      const token = await this.auth.accessToken();
      if (!this.current(context)) return;
      const receipt = await firstValueFrom(
        this.api.receipt(context.mode, action.caseId, action.actionId, action.kind, token),
      );
      if (!this.current(context)) return;
      this.acknowledge(receipt, context);
      this.notice.set('Сохранение этого действия подтверждено. Оно не заменяет текущее состояние обращения.');
    } catch (error) {
      if (!this.current(context)) return;
      if (error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403)) {
        this.deny(context);
        return;
      }
      this.error.set('Сохранение действия пока не подтверждено. Недоступная запись не разрешает повторную отправку.');
    }
  }
}
