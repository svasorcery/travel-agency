import { isPlatformBrowser } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { computed, effect, Injectable, inject, PLATFORM_ID, signal, untracked } from '@angular/core';
import {
  decodeTripWrite,
  type ReplaceTrip,
  type TripBlock,
  type TripBrief,
  type TripPlan,
  type TripSummary,
  TripsApiService,
  tripDates,
  tripGuid,
  tripTotals,
  tripWrite,
} from '@travel/api-client';
import { firstValueFrom, type Observable, Subject, takeUntil } from 'rxjs';
import { TripsAuthService } from './trips-auth.service';
import { TripsAvailabilityService } from './trips-availability.service';
import { tripsProblemCode } from './trips-problem';
export type TripsEditorStatus =
  | 'idle'
  | 'loading'
  | 'ready'
  | 'empty'
  | 'unauthenticated'
  | 'unavailable'
  | 'disabled'
  | 'invalid'
  | 'conflict'
  | 'uncertain'
  | 'not-found';
interface Ticket {
  session: string | null;
  plan: number;
  read?: number;
  edit: number;
}
@Injectable({ providedIn: 'root' })
export class TripsStateService {
  readonly auth = inject(TripsAuthService);
  readonly availability = inject(TripsAvailabilityService);
  private readonly api = inject(TripsApiService);
  private readonly platform = inject(PLATFORM_ID);
  private sessionKey: string | null = null;
  private readonly sessionGeneration = signal(0);
  private planEpoch = 0;
  private readEpoch = 0;
  private listEpoch = 0;
  private editVersion = 0;
  private highest = 0n;
  private createCapacityBlocked = false;
  private readonly formDirty = signal(false);
  private cancel = new Subject<void>();
  private readonly saved = signal<TripPlan | null>(null);
  private readonly draft = signal<ReplaceTrip | null>(null);
  private readonly compare = signal<TripPlan | null>(null);
  private readonly items = signal<TripSummary[]>([]);
  private readonly id = signal<string | null>(null);
  private initial = '';
  readonly status = signal<TripsEditorStatus>('idle');
  readonly message = signal('');
  readonly pending = signal<'create' | 'save' | 'delete' | null>(null);
  private readonly barrier = signal<'uncertain' | 'conflict' | null>(null);
  readonly mutationBlocked = computed(() => this.barrier() !== null);
  readonly nextCursor = signal<string | null>(null);
  readonly authorized = computed(() => {
    this.sessionGeneration();
    const current = this.currentSession();
    return current !== null && this.sessionKey === current;
  });
  readonly baseline = computed(() => (this.authorized() ? this.saved() : null));
  readonly buffer = computed(() => (this.authorized() ? this.draft() : null));
  readonly comparison = computed(() => (this.authorized() ? this.compare() : null));
  readonly rows = computed(() => (this.authorized() ? this.items() : []));
  readonly selectedId = computed(() => (this.authorized() ? this.id() : null));
  readonly dirty = computed(() => {
    const d = this.buffer();
    return (
      d !== null &&
      (this.formDirty() ||
        JSON.stringify(d) !== (this.saved() ? JSON.stringify(tripWrite(this.saved()!)) : this.initial))
    );
  });
  readonly totals = computed(() => {
    const d = this.buffer();
    try {
      return d ? tripTotals(d) : null;
    } catch {
      return null;
    }
  });
  constructor() {
    effect(() => {
      const key = this.currentSession();
      untracked(() => {
        if (key !== this.sessionKey) this.reset(key);
      });
    });
  }
  private currentSession(): string | null {
    const s = this.auth.status();
    return s.kind === 'authenticated' ? `${this.auth.identityEpoch()}:${s.userId.toLowerCase()}` : null;
  }
  private reset(key: string | null): void {
    this.cancel.next();
    this.sessionKey = key;
    this.sessionGeneration.update((v) => v + 1);
    this.planEpoch++;
    this.readEpoch++;
    this.listEpoch++;
    this.highest = 0n;
    this.saved.set(null);
    this.draft.set(null);
    this.compare.set(null);
    this.items.set([]);
    this.id.set(null);
    this.initial = '';
    this.formDirty.set(false);
    this.createCapacityBlocked = false;
    this.pending.set(null);
    this.barrier.set(null);
    this.nextCursor.set(null);
    this.message.set('');
    this.status.set(key === null ? 'unauthenticated' : 'idle');
    if (key === null) this.availability.clearPrivate();
  }
  private sync(): boolean {
    const key = this.currentSession();
    if (key !== this.sessionKey) this.reset(key);
    return key !== null;
  }
  private ticket(read = false): Ticket {
    return {
      session: this.sessionKey,
      plan: this.planEpoch,
      read: read ? ++this.readEpoch : undefined,
      edit: this.editVersion,
    };
  }
  private current(t: Ticket): boolean {
    return (
      t.session !== null &&
      t.session === this.currentSession() &&
      t.session === this.sessionKey &&
      t.plan === this.planEpoch &&
      (t.read === undefined || t.read === this.readEpoch)
    );
  }
  private async request<T>(t: Ticket, call: (token: string | null) => Observable<T>): Promise<T> {
    const token = await this.auth.accessToken();
    if (!this.current(t)) throw new Error('Session changed.');
    return firstValueFrom(call(token).pipe(takeUntil(this.cancel)));
  }
  async enter(id: string | null): Promise<void> {
    if (!isPlatformBrowser(this.platform)) return;
    if (this.auth.hasOidcCallback()) await this.auth.initializeFromCallback();
    if (!this.sync()) {
      await this.availability.initialize(null);
      return;
    }
    this.planEpoch++;
    this.readEpoch++;
    this.pending.set(null);
    try {
      this.id.set(id === null ? null : tripGuid(id));
    } catch {
      this.clearEditor();
      this.status.set('not-found');
      this.message.set('Неверная ссылка на план.');
      return;
    }
    this.barrier.set(null);
    this.saved.set(null);
    this.draft.set(null);
    this.compare.set(null);
    this.highest = 0n;
    this.initial = '';
    this.formDirty.set(false);
    this.createCapacityBlocked = false;
    this.status.set('loading');
    const t = this.ticket();
    try {
      const token = await this.auth.accessToken();
      if (!this.current(t)) return;
      await this.availability.initialize(token);
      if (!this.current(t)) return;
      if (this.availability.status() !== 'ready') {
        this.status.set(this.availability.status() === 'disabled' ? 'disabled' : 'unavailable');
        return;
      }
      if (!(await this.loadList(false))) return;
      if (!this.current(t)) return;
      if (id !== null) await this.refreshDetail();
      else {
        this.newPlan();
        this.status.set(this.items().length === 0 ? 'empty' : 'ready');
      }
    } catch (e) {
      if (this.current(t)) this.failure(e, false);
    }
  }
  newPlan(): void {
    if (!this.sync() || this.pending() || this.barrier() !== null) return;
    this.planEpoch++;
    this.readEpoch++;
    this.highest = 0n;
    this.saved.set(null);
    this.compare.set(null);
    this.id.set(null);
    const draft: ReplaceTrip = {
      title: '',
      brief: {
        destinationId: 'saint-petersburg',
        originId: null,
        startDate: '',
        endDate: '',
        adults: 1,
        currency: 'RUB',
        totalBudget: null,
        interests: [],
        pace: 'balanced',
      },
      days: [],
    };
    this.draft.set(draft);
    this.initial = JSON.stringify(draft);
    this.formDirty.set(false);
    this.message.set('');
    this.status.set('ready');
  }
  touch(unappliedBrief = true): void {
    if (this.sync() && this.pending() !== 'delete') {
      this.editVersion++;
      this.formDirty.set(unappliedBrief);
    }
  }
  edit(value: ReplaceTrip): void {
    if (!this.sync() || this.pending() === 'delete') return;
    this.editVersion++;
    this.draft.set(structuredClone(value));
  }
  changeBrief(brief: TripBrief): boolean {
    const d = this.buffer();
    if (!d || this.pending() === 'delete') return false;
    const saved = this.baseline();
    if (
      saved?.days.some((day) => day.blocks.length) &&
      (brief.destinationId !== saved.brief.destinationId || brief.currency !== saved.brief.currency)
    ) {
      this.validation(
        'Сначала сохраните удаление всех блоков. Затем измените город или валюту. Можно отменить изменения параметров, сохранив правки блоков.',
      );
      return false;
    }
    const blocks = d.days.flatMap((day) => day.blocks);
    if (blocks.length && (brief.destinationId !== d.brief.destinationId || brief.currency !== d.brief.currency)) {
      this.validation('Сначала удалите все блоки, чтобы изменить город или валюту.');
      return false;
    }
    let dates: string[];
    try {
      dates = tripDates(brief.startDate, brief.endDate);
    } catch {
      this.validation('Укажите корректный период от 1 до 14 календарных дней.');
      return false;
    }
    if (saved?.days.some((day) => !dates.includes(day.date) && day.blocks.length)) {
      this.validation(
        'Сначала перенесите или удалите блоки исключаемых дней и сохраните эти изменения. Затем сократите период.',
      );
      return false;
    }
    if (d.days.some((day) => !dates.includes(day.date) && day.blocks.length)) {
      this.validation('Сначала перенесите или удалите блоки в исключаемых днях.');
      return false;
    }
    this.edit({
      ...d,
      brief,
      days: dates.map((date) => ({ date, blocks: d.days.find((day) => day.date === date)?.blocks ?? [] })),
    });
    this.formDirty.set(false);
    this.message.set('');
    return true;
  }
  isSavedDate(date: string): boolean {
    return this.baseline()?.days.some((day) => day.date === date) ?? false;
  }
  addBlock(date: string): void {
    if (!this.isSavedDate(date)) {
      this.validation('Сначала сохраните новые пустые дни. После этого можно добавлять или переносить в них блоки.');
      return;
    }
    const d = this.buffer(),
      day = d?.days.find((x) => x.date === date);
    if (!d || !day) return;
    if (day.blocks.length >= 8) {
      this.validation('В одном дне может быть до 8 блоков.');
      return;
    }
    this.edit({
      ...d,
      days: d.days.map((x) =>
        x.date === date
          ? {
              ...x,
              blocks: [
                ...x.blocks,
                {
                  blockId: crypto.randomUUID(),
                  title: 'Новый блок',
                  note: null,
                  startTime: null,
                  endTime: null,
                  estimate: null,
                },
              ],
            }
          : x,
      ),
    });
  }
  updateBlock(blockId: string, patch: Partial<Omit<TripBlock, 'blockId'>>): void {
    const d = this.buffer();
    if (d)
      this.edit({
        ...d,
        days: d.days.map((day) => ({
          ...day,
          blocks: day.blocks.map((b) => (b.blockId === blockId ? { ...b, ...patch } : b)),
        })),
      });
  }
  removeBlock(blockId: string): void {
    const d = this.buffer();
    if (d)
      this.edit({
        ...d,
        days: d.days.map((day) => ({ ...day, blocks: day.blocks.filter((b) => b.blockId !== blockId) })),
      });
  }
  moveBlock(blockId: string, date: string, index: number): void {
    const d = this.buffer();
    if (!d) return;
    const source = d.days.find((day) => day.blocks.some((b) => b.blockId === blockId)),
      target = d.days.find((day) => day.date === date),
      block = source?.blocks.find((b) => b.blockId === blockId);
    if (!source || !target || !block) return;
    if (!this.isSavedDate(date)) {
      this.validation('Сначала сохраните новые пустые дни. После этого можно переносить в них блоки.');
      return;
    }
    if (source !== target && target.blocks.length >= 8) {
      this.validation('В выбранном дне уже 8 блоков.');
      return;
    }
    const days = d.days.map((day) => ({ ...day, blocks: day.blocks.filter((b) => b.blockId !== blockId) }));
    const dest = days.find((day) => day.date === date)!;
    dest.blocks.splice(Math.max(0, Math.min(index, dest.blocks.length)), 0, block);
    this.edit({ ...d, days });
  }
  async loadList(append = false): Promise<boolean> {
    if (!this.sync()) return false;
    const t = this.ticket(),
      list = ++this.listEpoch,
      cursor = append ? this.nextCursor() : null;
    if (append && cursor === null) return false;
    try {
      const page = await this.request(t, (token) => this.api.list(cursor, token));
      if (!this.current(t) || list !== this.listEpoch) return false;
      const dedup = new Map((append ? this.items() : []).map((row) => [row.tripId, row]));
      for (const row of page.items) {
        const old = dedup.get(row.tripId);
        if (!old || BigInt(row.revision) >= BigInt(old.revision)) dedup.set(row.tripId, row);
      }
      this.items.set([...dedup.values()]);
      this.nextCursor.set(page.nextCursor);
      if (
        (this.barrier() === 'uncertain' || this.createCapacityBlocked) &&
        this.pending() === null &&
        this.id() === null
      ) {
        this.createCapacityBlocked = false;
        this.barrier.set(null);
        this.status.set('ready');
        this.message.set(
          'Показан текущий список. Он не подтверждает выполнение потерянной команды. Новое создание требует явного действия.',
        );
      }
      return true;
    } catch (e) {
      if (this.current(t) && list === this.listEpoch) this.failure(e, false);
      return false;
    }
  }
  async refreshDetail(): Promise<void> {
    const id = this.selectedId();
    if (!id || !this.sync()) return;
    const t = this.ticket(true),
      wasDirty = this.dirty(),
      wasBlocked = this.barrier() !== null;
    try {
      const p = await this.request(t, (token) => this.api.detail(id, token));
      if (!this.current(t) || BigInt(p.revision) < this.highest) return;
      this.highest = BigInt(p.revision);
      if (wasDirty || this.editVersion !== t.edit || wasBlocked) {
        this.compare.set(p);
        this.barrier.set('conflict');
        this.status.set('conflict');
        this.message.set(
          'Текущее состояние сервера получено. Сравните версии и явно выберите, какие правки сохранить.',
        );
      } else {
        this.accept(p, true);
        this.status.set('ready');
        this.message.set('Загружено сохранённое состояние.');
      }
    } catch (e) {
      if (!this.current(t)) return;
      if (e instanceof HttpErrorResponse && e.status === 404) {
        this.status.set('not-found');
        this.message.set('План сейчас недоступен. Локальные правки остаются до явного отказа от них.');
        if (!this.dirty()) this.clearEditor();
      } else this.failure(e, false);
    }
  }
  keepLocalAgainstCurrent(): void {
    const p = this.comparison();
    if (!p) return;
    this.saved.set(p);
    this.barrier.set(null);
    this.compare.set(null);
    this.status.set('ready');
    this.message.set('Локальные правки сохранены в редакторе. Нажмите «Сохранить» для отправки на текущей версии.');
  }
  useCurrentServer(): void {
    const p = this.comparison();
    if (!p) return;
    this.accept(p, true);
    this.barrier.set(null);
    this.compare.set(null);
    this.status.set('ready');
    this.message.set('Выбрано текущее сохранённое состояние.');
  }
  async create(): Promise<void> {
    if (this.selectedId() !== null) return;
    await this.mutate('create');
  }
  async save(): Promise<void> {
    if (this.selectedId() === null || !this.dirty()) return;
    await this.mutate('save');
  }
  async deletePlan(): Promise<void> {
    if (this.selectedId() === null) return;
    await this.mutate('delete');
  }
  private async mutate(kind: 'create' | 'save' | 'delete'): Promise<void> {
    if (!this.sync() || this.pending() || this.barrier() !== null || this.status() === 'not-found') return;
    const draft = this.buffer(),
      saved = this.baseline();
    if (!draft) return;
    let body: ReplaceTrip;
    try {
      body = kind === 'delete' ? draft : decodeTripWrite(draft);
    } catch {
      this.validation('Проверьте название, даты, участников, текст, время и суммы. Изменения не отправлены.');
      return;
    }
    const t = this.ticket(),
      id = this.selectedId();
    this.pending.set(kind);
    this.message.set('');
    try {
      if (kind === 'delete') {
        await this.request(t, (token) => this.api.delete(id!, saved!.revision, token));
        if (!this.current(t)) return;
        this.clearEditor();
        this.status.set('ready');
        this.message.set('План удалён. Это действие не отменяет бронирования.');
        await this.loadList();
        return;
      }
      const p = await this.request(t, (token) =>
        kind === 'create'
          ? this.api.create({ title: body.title, brief: body.brief }, token)
          : this.api.replace(id!, saved!.revision, tripWrite(body), token),
      );
      if (!this.current(t) || BigInt(p.revision) < this.highest) return;
      this.highest = BigInt(p.revision);
      this.accept(p, this.editVersion === t.edit);
      this.status.set('ready');
      this.message.set(
        this.editVersion === t.edit
          ? 'План сохранён.'
          : 'Отправленная версия сохранена. Более поздние правки ещё не сохранены.',
      );
      await this.loadList();
    } catch (e) {
      if (this.current(t)) this.failure(e, true);
    } finally {
      if (this.current(t)) this.pending.set(null);
    }
  }
  private accept(p: TripPlan, replace: boolean): void {
    this.saved.set(structuredClone(p));
    this.id.set(p.tripId);
    this.highest = BigInt(p.revision);
    if (replace) {
      this.draft.set(tripWrite(p));
      this.formDirty.set(false);
    }
    this.compare.set(null);
  }
  private clearEditor(): void {
    this.planEpoch++;
    this.readEpoch++;
    this.saved.set(null);
    this.draft.set(null);
    this.compare.set(null);
    this.id.set(null);
    this.highest = 0n;
    this.initial = '';
    this.formDirty.set(false);
    this.createCapacityBlocked = false;
    this.pending.set(null);
    this.barrier.set(null);
  }
  validation(message: string): void {
    this.message.set(message);
    this.status.set(this.barrier() ?? 'invalid');
  }
  private failure(error: unknown, mutation: boolean): void {
    const code = error instanceof HttpErrorResponse ? error.status : 0;
    if (code === 401 || code === 403) {
      this.auth.deny(this.auth.identityEpoch());
      this.reset(null);
      return;
    }
    if (!mutation && this.barrier() !== null) {
      this.status.set(this.barrier()!);
      this.message.set(
        'Перечитывание не удалось. Результат предыдущего действия всё ещё не подтверждён; повторная отправка заблокирована.',
      );
      return;
    }
    if (
      mutation &&
      this.pending() === 'create' &&
      code === 409 &&
      tripsProblemCode(error) === 'Trips.CapacityExceeded'
    ) {
      this.createCapacityBlocked = true;
      this.barrier.set('conflict');
      this.status.set('conflict');
      this.message.set(
        'Достигнут предел сохранённых планов. Обновите список; после освобождения места новое создание потребует явного действия.',
      );
      return;
    }
    if (code === 412 || code === 409) {
      this.barrier.set('conflict');
      this.status.set('conflict');
      this.message.set(
        'Сервер отклонил изменение. Перечитайте текущий план для явной сверки; ваши правки сохранены в редакторе.',
      );
    } else if (code === 400 || code === 428) {
      this.validation('Изменения отклонены. Проверьте данные и текущую версию.');
    } else if (mutation && (code === 0 || code >= 500 || !(error instanceof HttpErrorResponse))) {
      this.barrier.set('uncertain');
      this.status.set('uncertain');
      this.message.set(
        'Ответ не подтверждён. Действие могло выполниться. Автоматического повтора нет. Сначала перечитайте текущее состояние.',
      );
    } else {
      this.status.set(code === 404 ? 'not-found' : 'unavailable');
      this.message.set(
        code === 404 ? 'План недоступен.' : 'Хранилище сейчас недоступно. Локальные правки остаются в редакторе.',
      );
    }
  }
  leave(): void {
    this.reset(null);
  }
  async logout(): Promise<void> {
    this.reset(null);
    await this.auth.logout();
  }
}
