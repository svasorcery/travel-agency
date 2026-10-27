import { isPlatformBrowser } from '@angular/common';
import {
  computed,
  DestroyRef,
  effect,
  Injectable,
  InjectionToken,
  Injector,
  inject,
  PLATFORM_ID,
  signal,
  untracked,
} from '@angular/core';
import {
  type TripBrief,
  type TripBriefCandidate,
  type TripBriefCapability,
  type TripBriefRequest,
  type TripBriefSlot,
  type TripBriefZone,
  TripsBriefApiService,
  TripsBriefRequestError,
  tripBriefPolicyReviewed,
  tripBriefRequest,
  tripBriefSlots,
  tripCities,
  tripDate,
  tripDates,
  tripInterests,
  tripPaces,
} from '@travel/api-client';
import { firstValueFrom, Subject, takeUntil } from 'rxjs';
import { TripsStateService } from './trips-state.service';
export const TRIPS_BRIEF_MONOTONIC = new InjectionToken<() => number>('Trips brief monotonic clock', {
  providedIn: 'root',
  factory: () => () => performance.now(),
});
export type TripsBriefExtractionStatus =
  | 'idle'
  | 'checking'
  | 'ready'
  | 'pending'
  | 'review'
  | 'confirming'
  | 'transferred'
  | 'stale'
  | 'expired'
  | 'disabled'
  | 'busy'
  | 'deadline'
  | 'unavailable'
  | 'contract-error'
  | 'invalid'
  | 'oversize'
  | 'unauthenticated'
  | 'forbidden';
export type BriefTransfer = 'destinationId' | 'dates' | 'adults' | 'interests' | 'pace';
type Review = Record<TripBriefSlot, unknown>;
type Attempt = {
  id: number;
  ticket: object;
  draft: TripBrief;
  text: string;
  zone: TripBriefZone | null;
  cancel: Subject<void>;
  started: number;
  policy: string;
};
const unchecked = () => ({ destinationId: false, dates: false, adults: false, interests: false, pace: false });
@Injectable()
export class TripsBriefExtractionStateService {
  readonly editor = inject(TripsStateService);
  private readonly injector = inject(Injector);
  private readonly platform = inject(PLATFORM_ID);
  private readonly now = inject(TRIPS_BRIEF_MONOTONIC);
  readonly draft = signal<TripBrief | null>(null);
  readonly blocked = signal(false);
  readonly text = signal('');
  readonly zone = signal<TripBriefZone | null>('Europe/Moscow');
  readonly capability = signal<TripBriefCapability | null>(null);
  readonly phase = signal<TripsBriefExtractionStatus>('idle');
  readonly pending = signal(false);
  /** Local navigation guard only: never touches M1 dirty state or persists private content. */
  readonly unsaved = computed(
    () => this.text().length > 0 || this.value() !== null || (this.pending() && this.attempt !== null),
  );
  readonly review = signal<Review>({
    destinationId: '',
    startDate: '',
    endDate: '',
    durationDays: null,
    adults: null,
    interests: null,
    pace: '',
  });
  readonly selected = signal(unchecked());
  readonly corrected = signal<Partial<Record<TripBriefSlot, boolean>>>({});
  readonly reconcileDates = signal(false);
  readonly transferred = new Subject<TripBrief>();
  readonly message = computed(
    () =>
      ({
        unauthenticated: 'Для разбора войдите снова. Ручная форма остаётся доступна.',
        forbidden: 'Разбор недоступен этой учётной записи.',
        oversize: 'Текст превышает ограничения разбора. Измените его вручную.',
        invalid: 'Проверьте текст: нужны корректные символы, до 1000 символов Unicode и 4096 байт.',
        idle: 'Ручная форма доступна.',
        checking: 'Проверяем условия разбора…',
        ready: 'Условия доступны. Проверьте обработчика перед отправкой.',
        pending: 'Разбираем текст. Правки редактора отменят предложение.',
        review: 'Проверьте каждое поле и выберите только нужные.',
        confirming: 'Проверяем условия и версию плана. Ручная форма доступна.',
        transferred: 'Выбранные поля перенесены только в форму. Отдельно примените параметры и сохраните план.',
        stale: 'Предложение устарело. Проверьте текст и явно запросите новый разбор.',
        expired: 'Срок разбора истёк. Текст и предложение удалены из этого просмотра.',
        disabled: 'Разбор выключен. Ручная форма доступна.',
        busy: 'Разбор занят. Автоматического повтора нет.',
        deadline: 'Время ожидания истекло. Автоматического повтора нет.',
        unavailable: 'Разбор недоступен. Ручная форма доступна.',
        'contract-error': 'Ответ разбора не прошёл проверку. Ручная форма доступна.',
      })[this.phase()] ?? 'Разбор недоступен. Ручная форма доступна.',
  );
  private value = signal<TripBriefCandidate | null>(null);
  private attempt: Attempt | null = null;
  private sequence = 0;
  private reviewVersion = 0;
  private capabilityRead = 0;
  private timer: ReturnType<typeof setTimeout> | null = null;
  private scope: object | null = null;
  constructor() {
    effect(() => {
      const scope = this.editor.itineraryContext();
      this.pending();
      this.value();
      const attempt = this.attempt;
      const valid = !attempt || this.editor.isBriefTicketCurrent(attempt.ticket);
      untracked(() => {
        if (scope !== this.scope) {
          this.scope = scope;
          this.leave();
        } else if (!valid) this.invalidate('stale');
      });
    });
    inject(DestroyRef).onDestroy(() => {
      this.leave();
      this.transferred.complete();
    });
  }
  setDraft(value: TripBrief | null): void {
    if (this.draft() !== value && this.attempt) this.invalidate('stale');
    this.draft.set(value);
  }
  setBlocked(value: boolean): void {
    this.blocked.set(value);
    if (value && this.attempt) this.invalidate('stale');
  }
  textChanged(text: string): void {
    this.invalidate('idle');
    this.text.set(text);
  }
  zoneChanged(zone: TripBriefZone | null): void {
    this.invalidate('idle');
    this.zone.set(zone);
  }
  candidate(): TripBriefCandidate | null {
    if (this.attempt && (!this.current(this.attempt) || !this.fresh())) {
      this.invalidate(this.fresh() ? 'stale' : 'expired');
      return null;
    }
    return this.value();
  }
  canExtract(): boolean {
    return (
      isPlatformBrowser(this.platform) &&
      !this.pending() &&
      !this.blocked() &&
      !!this.draft() &&
      this.capability()?.status === 'configured' &&
      this.editor.captureBriefTicket() !== null
    );
  }
  private api(): TripsBriefApiService {
    return this.injector.get(TripsBriefApiService);
  }
  async refreshCapability(): Promise<void> {
    if (!isPlatformBrowser(this.platform) || this.pending()) return;
    this.invalidate('checking');
    const read = ++this.capabilityRead,
      scope = this.editor.itineraryContext();
    this.pending.set(true);
    this.capability.set(null);
    try {
      const token = await this.editor.auth.accessToken();
      if (read !== this.capabilityRead || scope !== this.editor.itineraryContext()) return;
      const value = await firstValueFrom(this.api().capability(token));
      const reviewed = await tripBriefPolicyReviewed(value);
      if (read !== this.capabilityRead || scope !== this.editor.itineraryContext()) return;
      this.capability.set(reviewed ? value : null);
      this.phase.set(reviewed ? 'ready' : 'unavailable');
    } catch (error) {
      if (read === this.capabilityRead) {
        this.capability.set(null);
        this.failure(error);
      }
    } finally {
      if (read === this.capabilityRead) this.pending.set(false);
    }
  }
  async extract(): Promise<void> {
    if (!this.canExtract()) return;
    const draft = this.draft()!,
      ticket = this.editor.captureBriefTicket(),
      cap = this.capability();
    if (!ticket || !cap?.processingPolicyVersion) return;
    let request: TripBriefRequest;
    try {
      request = tripBriefRequest({
        requestId: crypto.randomUUID(),
        text: this.text(),
        locale: 'ru',
        referenceZone: this.zone(),
        processingPolicyVersion: cap.processingPolicyVersion,
      });
    } catch {
      this.phase.set('invalid');
      return;
    }
    this.invalidate('pending');
    const op: Attempt = {
      id: ++this.sequence,
      ticket,
      draft,
      text: request.text,
      zone: request.referenceZone,
      cancel: new Subject<void>(),
      started: this.now(),
      policy: request.processingPolicyVersion,
    };
    this.attempt = op;
    this.pending.set(true);
    try {
      const token = await this.editor.auth.accessToken();
      if (!this.current(op)) return;
      const value = await firstValueFrom(this.api().candidate(request, token).pipe(takeUntil(op.cancel)));
      if (!this.current(op) || value.requestId !== request.requestId || value.processingPolicyVersion !== op.policy)
        return;
      this.value.set(value);
      if (!this.fresh()) {
        this.invalidate('expired');
        return;
      }
      const review = {} as Review;
      for (const key of tripBriefSlots)
        review[key] =
          value.fields[key].state === 'extracted'
            ? structuredClone(value.fields[key].value)
            : key === 'interests'
              ? null
              : key === 'adults' || key === 'durationDays'
                ? null
                : '';
      this.review.set(review);
      this.corrected.set({});
      this.selected.set(unchecked());
      this.reconcileDates.set(false);
      this.phase.set('review');
      const lifetime = Date.parse(value.expiresAt) - Date.parse(value.referenceInstant) - (this.now() - op.started);
      this.timer = setTimeout(
        () => {
          if (this.attempt === op) this.invalidate('expired');
        },
        Math.max(0, lifetime),
      );
    } catch (error) {
      if (this.attempt === op) {
        if (this.current(op)) {
          this.invalidate('idle');
          this.failure(error);
        } else this.invalidate('stale');
      }
    } finally {
      if (this.attempt === op) this.pending.set(false);
    }
  }
  correct(key: TripBriefSlot, value: unknown): void {
    if (!this.candidate()) return;
    this.reviewVersion++;
    this.review.update((r) => ({ ...r, [key]: value }));
    this.corrected.update((c) => ({ ...c, [key]: true }));
    if (key === 'startDate' || key === 'endDate') this.reconcileDates.set(false);
  }
  select(key: BriefTransfer, checked: boolean): void {
    if (!this.candidate()) return;
    this.reviewVersion++;
    this.selected.update((s) => ({ ...s, [key]: checked }));
  }
  reconcile(value: boolean): void {
    if (!this.candidate()) return;
    this.reviewVersion++;
    this.reconcileDates.set(value);
  }
  deriveEnd(): void {
    const r = this.review(),
      duration = r.durationDays;
    if (
      !this.candidate() ||
      typeof duration !== 'number' ||
      !Number.isInteger(duration) ||
      duration < 1 ||
      duration > 14 ||
      (r.endDate && !this.reconcileDates())
    )
      return;
    try {
      const start = tripDate(r.startDate);
      const end = new Date(Date.parse(start + 'T00:00:00Z') + (duration - 1) * 86400000).toISOString().slice(0, 10);
      tripDate(end);
      this.correct('endDate', end);
      this.reconcileDates.set(true);
    } catch {
      /* Date correction remains explicit. */
    }
  }
  valid(choice: BriefTransfer): boolean {
    const value = this.value();
    if (!value) return false;
    const r = this.review(),
      corrected = this.corrected();
    const slots: TripBriefSlot[] = choice === 'dates' ? ['startDate', 'endDate'] : [choice];
    if (slots.some((key) => value.fields[key].state !== 'extracted' && !corrected[key])) return false;
    if (value.issues.some((issue) => issue.slots.some((key) => slots.includes(key) && !corrected[key]))) return false;
    try {
      switch (choice) {
        case 'destinationId':
          return tripCities.includes(r.destinationId as never);
        case 'adults':
          return typeof r.adults === 'number' && Number.isInteger(r.adults) && r.adults >= 1 && r.adults <= 9;
        case 'pace':
          return tripPaces.includes(r.pace as never);
        case 'interests':
          return (
            Array.isArray(r.interests) &&
            r.interests.length <= 5 &&
            new Set(r.interests).size === r.interests.length &&
            r.interests.every((x) => tripInterests.includes(x))
          );
        case 'dates': {
          const dates = tripDates(tripDate(r.startDate), tripDate(r.endDate));
          return typeof r.durationDays !== 'number' || dates.length === r.durationDays || this.reconcileDates();
        }
      }
    } catch {
      return false;
    }
  }
  canConfirm(): boolean {
    const selected = this.selected();
    return (
      !this.pending() &&
      !this.blocked() &&
      !!this.candidate() &&
      Object.entries(selected).some(([, v]) => v) &&
      Object.entries(selected).every(([key, v]) => !v || this.valid(key as BriefTransfer))
    );
  }
  async confirm(): Promise<void> {
    if (!this.canConfirm()) return;
    const op = this.attempt!,
      value = this.value()!,
      version = this.reviewVersion,
      read = ++this.capabilityRead;
    this.pending.set(true);
    this.phase.set('confirming');
    try {
      const token = await this.editor.auth.accessToken();
      if (!this.current(op)) return;
      const [owner, cap] = await Promise.all([
        this.editor.readBriefOwner(op.ticket, op.cancel),
        firstValueFrom(this.api().capability(token).pipe(takeUntil(op.cancel))),
      ]);
      const reviewed = await tripBriefPolicyReviewed(cap);
      if (
        !this.current(op) ||
        this.value() !== value ||
        version !== this.reviewVersion ||
        read !== this.capabilityRead ||
        !this.fresh()
      ) {
        if (this.attempt === op) this.invalidate('stale');
        return;
      }
      if (!owner || !reviewed || cap.processingPolicyVersion !== op.policy) {
        this.capability.set(null);
        this.invalidate('stale');
        return;
      }
      const selected = this.selected();
      if (
        !Object.entries(selected).some(([, v]) => v) ||
        !Object.entries(selected).every(([key, v]) => !v || this.valid(key as BriefTransfer))
      )
        return;
      const r = this.review(),
        next = { ...op.draft };
      if (selected.destinationId) next.destinationId = r.destinationId as TripBrief['destinationId'];
      if (selected.dates) {
        next.startDate = r.startDate as string;
        next.endDate = r.endDate as string;
      }
      if (selected.adults) next.adults = r.adults as number;
      if (selected.interests) next.interests = [...(r.interests as TripBrief['interests'])];
      if (selected.pace) next.pace = r.pace as TripBrief['pace'];
      // Final synchronous ticket check follows every await; only the caller's form receives this value.
      if (!this.current(op) || version !== this.reviewVersion || !this.fresh()) return;
      this.invalidate('transferred');
      this.text.set('');
      this.transferred.next(next);
    } catch (error) {
      if (this.attempt === op) {
        this.capability.set(null);
        this.invalidate('idle');
        this.failure(error);
      }
    } finally {
      if (this.attempt === op) {
        this.pending.set(false);
        if (this.phase() === 'confirming') this.phase.set('review');
      }
    }
  }
  private current(op: Attempt): boolean {
    return (
      this.attempt === op &&
      this.sequence === op.id &&
      this.editor.isBriefTicketCurrent(op.ticket) &&
      this.draft() === op.draft &&
      this.text() === op.text &&
      this.zone() === op.zone &&
      !this.blocked()
    );
  }
  private fresh(): boolean {
    const value = this.value(),
      op = this.attempt;
    if (!value || !op) return true;
    const elapsed = this.now() - op.started;
    return (
      Number.isFinite(elapsed) &&
      elapsed >= 0 &&
      elapsed < Date.parse(value.expiresAt) - Date.parse(value.referenceInstant)
    );
  }
  private failure(error: unknown): void {
    this.text.set('');
    this.phase.set(error instanceof TripsBriefRequestError ? error.kind : 'unavailable');
  }
  private invalidate(phase: TripsBriefExtractionStatus): void {
    this.sequence++;
    this.reviewVersion++;
    if (this.timer !== null) clearTimeout(this.timer);
    this.timer = null;
    const op = this.attempt;
    this.attempt = null;
    op?.cancel.next();
    op?.cancel.complete();
    this.value.set(null);
    this.selected.set(unchecked());
    this.review.set({
      destinationId: '',
      startDate: '',
      endDate: '',
      durationDays: null,
      adults: null,
      interests: null,
      pace: '',
    });
    this.corrected.set({});
    this.pending.set(false);
    this.phase.set(phase);
    if (phase === 'expired' || phase === 'stale') this.text.set('');
  }
  suspend(): void {
    this.leave();
  }
  leave(): void {
    this.capabilityRead++;
    this.invalidate('idle');
    this.text.set('');
    this.capability.set(null);
  }
}
