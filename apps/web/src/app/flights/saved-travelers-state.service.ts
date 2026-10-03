import { HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, effect, Injectable, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationStart, Router } from '@angular/router';
import {
  type DemoTravelerOwner,
  type SavedTraveler,
  type SavedTravelerDetails,
  type SavedTravelerFieldError,
  type SavedTravelerReceipt,
  SavedTravelersApiService,
  safeTravelerFieldErrors,
} from '@travel/api-client';
import { firstValueFrom, map, type Observable, Subject, takeUntil } from 'rxjs';
import { FlightsAuthService } from './flights-auth.service';
export interface TravelerSession {
  owner: string;
  identityEpoch: number;
  authGeneration: number;
}
export type TravelerLoadState = 'idle' | 'loading' | 'ready' | 'empty' | 'unavailable' | 'auth' | 'missing';
export type TravelerMutationState =
  | 'idle'
  | 'pending'
  | 'saved'
  | 'validation'
  | 'conflict'
  | 'missing'
  | 'refused'
  | 'unknown';
interface UncertainMutation {
  id: string;
  kind: 'create' | 'update' | 'delete';
}
class TravelerTokenError extends Error {}
const KNOWN: Readonly<Record<string, number>> = {
  'Flights.TravelerInvalid': 400,
  'Flights.TravelerPageInvalid': 400,
  'Flights.TravelerIdInvalid': 400,
  'Flights.TravelerPreconditionInvalid': 400,
  'Flights.TravelerNotFound': 404,
  'Flights.TravelerPreconditionFailed': 412,
  'Flights.TravelerPreconditionRequired': 428,
  'Flights.RequestTooLarge': 413,
};
/** Component-scoped PII; navigation and auth changes erase it. No write is retried. */
@Injectable()
export class SavedTravelersState {
  private readonly auth = inject(FlightsAuthService);
  private readonly api = inject(SavedTravelersApiService);
  private readonly cancelled = new Subject<void>();
  private signature: string | null = null;
  private lastOwnerSignature: string | null = null;
  private authGeneration = 0;
  private alive = true;
  private pageGeneration = 0;
  private reviewedId: string | null = null;
  private dispatched: UncertainMutation | null = null;
  readonly items = signal<SavedTraveler[]>([]);
  readonly privacyGeneration = signal(0);
  readonly accessDenied = signal(false);
  readonly loadState = signal<TravelerLoadState>('idle');
  readonly hasMore = signal(false);
  readonly offset = signal(0);
  readonly mutationId = signal<string | null>(null);
  readonly mutationState = signal<TravelerMutationState>('idle');
  readonly fieldErrors = signal<SavedTravelerFieldError[]>([]);
  readonly uncertain = signal<UncertainMutation[]>([]);
  readonly message = signal('');
  constructor() {
    this.syncAuth();
    effect(() => {
      this.auth.status();
      this.auth.identityEpoch?.();
      untracked(() => this.syncAuth());
    });
    inject(Router)
      .events.pipe(takeUntilDestroyed())
      .subscribe((event) => {
        if (event instanceof NavigationStart) {
          this.alive = false;
          this.clear();
        }
      });
    inject(DestroyRef).onDestroy(() => {
      this.alive = false;
      this.clear();
      this.cancelled.complete();
    });
  }
  private syncAuth(): void {
    const auth = this.auth.status();
    const signature =
      auth.kind === 'authenticated' ? `${auth.userId.toLowerCase()}:${this.auth.identityEpoch?.() ?? 0}` : null;
    if (signature !== this.signature) {
      const old = this.lastOwnerSignature;
      this.clear();
      if (old !== null && signature !== null && old !== signature) this.uncertain.set([]);
      this.signature = signature;
      this.accessDenied.set(false);
      if (signature !== null) this.lastOwnerSignature = signature;
    }
    if (signature === null) this.loadState.set('auth');
  }
  private clear(): void {
    if (this.dispatched !== null) this.rememberUnknown(this.dispatched);
    this.dispatched = null;
    this.authGeneration++;
    this.pageGeneration++;
    this.privacyGeneration.update((value) => value + 1);
    this.cancelled.next();
    this.items.set([]);
    this.hasMore.set(false);
    this.offset.set(0);
    this.loadState.set('idle');
    this.fieldErrors.set([]);
    this.reviewedId = null;
    if (this.mutationState() === 'pending') this.mutationState.set(this.uncertain().length ? 'unknown' : 'idle');
  }
  session(): TravelerSession | null {
    this.syncAuth();
    const auth = this.auth.status();
    return this.alive && !this.accessDenied() && auth.kind === 'authenticated'
      ? {
          owner: auth.userId.toLowerCase(),
          identityEpoch: this.auth.identityEpoch?.() ?? 0,
          authGeneration: this.authGeneration,
        }
      : null;
  }
  current(session: TravelerSession): boolean {
    const current = this.session();
    return (
      current !== null &&
      current.owner === session.owner &&
      current.identityEpoch === session.identityEpoch &&
      current.authGeneration === session.authGeneration
    );
  }
  private demoOwner(session: TravelerSession): DemoTravelerOwner | undefined {
    return this.auth.isDemo && (session.owner === 'demo-only' || session.owner === 'demo-other')
      ? session.owner
      : undefined;
  }
  private known(error: unknown): { status: number; code: string } | null {
    if (!(error instanceof HttpErrorResponse)) return null;
    if (error.status === 401 || error.status === 403) return { status: error.status, code: 'auth' };
    const value = error.error;
    const type = value && typeof value === 'object' && typeof value.type === 'string' ? value.type : '';
    const code = type.startsWith('https://travel.local/errors/')
      ? type.slice('https://travel.local/errors/'.length)
      : '';
    return Object.hasOwn(KNOWN, code) && KNOWN[code] === error.status ? { status: error.status, code } : null;
  }
  private authDenied(): void {
    this.clear();
    this.accessDenied.set(true);
    this.loadState.set('auth');
    this.message.set('Войдите снова, чтобы открыть сохранённых пассажиров.');
  }
  reauthorize(): void {
    this.clear();
    this.accessDenied.set(false);
  }
  private requireToken(token: string | null): void {
    if (token === null && !this.auth.isDemo) throw new TravelerTokenError();
  }
  async load(offset = 0): Promise<void> {
    const session = this.session();
    if (session === null || this.loadState() === 'loading') return;
    const page = ++this.pageGeneration;
    this.loadState.set('loading');
    try {
      const token = await this.auth.accessToken();
      this.requireToken(token);
      if (!this.current(session) || page !== this.pageGeneration) return;
      const result = await firstValueFrom(
        this.api.list(offset, token, this.demoOwner(session)).pipe(takeUntil(this.cancelled)),
      );
      if (!this.current(session) || page !== this.pageGeneration) return;
      const combined = offset === 0 ? result.items : [...this.items(), ...result.items];
      if (new Set(combined.map((item) => item.id)).size !== combined.length) throw new Error('Invalid profile page.');
      this.items.set(combined);
      this.offset.set(offset);
      this.hasMore.set(result.hasMore);
      this.loadState.set(combined.length ? 'ready' : 'empty');
    } catch (error) {
      if (!this.current(session) || page !== this.pageGeneration) return;
      const known = this.known(error);
      if (
        error instanceof TravelerTokenError ||
        known?.status === 401 ||
        known?.status === 403 ||
        this.auth.status().kind !== 'authenticated'
      )
        this.authDenied();
      else {
        this.items.set([]);
        this.hasMore.set(false);
        this.loadState.set('unavailable');
        this.message.set('Профили недоступны. Проверьте соединение и доступность защищённых данных.');
      }
    }
  }
  async read(id: string): Promise<SavedTraveler | null> {
    const session = this.session();
    if (session === null) return null;
    try {
      const token = await this.auth.accessToken();
      this.requireToken(token);
      if (!this.current(session)) return null;
      const profile = await firstValueFrom(
        this.api.get(id, token, this.demoOwner(session)).pipe(takeUntil(this.cancelled)),
      );
      if (!this.current(session)) return null;
      if (id === this.mutationId()) this.reviewedId = id;
      return profile;
    } catch (error) {
      if (!this.current(session)) return null;
      const known = this.known(error);
      if (error instanceof TravelerTokenError || known?.status === 401 || known?.status === 403) this.authDenied();
      else if (known?.status === 404) {
        this.items.update((items) => items.filter((item) => item.id !== id.toLowerCase()));
        this.loadState.set('missing');
        this.message.set('Профиль сейчас не виден. Это не подтверждает результат предыдущей операции.');
        if (id === this.mutationId()) this.reviewedId = id;
      } else {
        this.message.set('Не удалось прочитать защищённый профиль. Попробуйте проверить текущее состояние позже.');
      }
      return null;
    }
  }
  acceptReview(): void {
    if (this.reviewedId !== null && this.reviewedId === this.mutationId() && this.mutationState() !== 'pending') {
      this.mutationState.set('idle');
      this.fieldErrors.set([]);
      this.message.set(
        'Текущее состояние прочитано. Проверьте данные перед новой условной операцией; прежний результат не подтверждён.',
      );
    }
  }
  canMutate(): boolean {
    return this.session() !== null && !['pending', 'unknown', 'conflict', 'missing'].includes(this.mutationState());
  }
  create(details: SavedTravelerDetails): Promise<SavedTravelerReceipt | null> {
    if (!this.canMutate()) return Promise.resolve(null);
    const id = crypto.randomUUID();
    this.mutationId.set(id);
    return this.mutate({ id, kind: 'create' }, null, details);
  }
  update(id: string, revision: string, details: SavedTravelerDetails): Promise<SavedTravelerReceipt | null> {
    if (!this.canMutate()) return Promise.resolve(null);
    this.mutationId.set(id);
    return this.mutate({ id, kind: 'update' }, revision, details);
  }
  delete(id: string, revision: string): Promise<SavedTravelerReceipt | null> {
    if (!this.canMutate()) return Promise.resolve(null);
    this.mutationId.set(id);
    return this.mutate({ id, kind: 'delete' }, revision, null);
  }
  private rememberUnknown(mutation: UncertainMutation): void {
    if (!this.uncertain().some((item) => item.id === mutation.id && item.kind === mutation.kind))
      this.uncertain.update((items) => [...items, mutation]);
  }
  private async mutate(
    mutation: UncertainMutation,
    revision: string | null,
    details: SavedTravelerDetails | null,
  ): Promise<SavedTravelerReceipt | null> {
    const session = this.session();
    if (session === null) return null;
    this.mutationState.set('pending');
    this.fieldErrors.set([]);
    this.reviewedId = null;
    this.message.set('');
    let dispatched = false;
    try {
      const token = await this.auth.accessToken();
      this.requireToken(token);
      if (!this.current(session)) return null;
      let request: Observable<SavedTravelerReceipt>;
      if (mutation.kind === 'delete') {
        if (revision === null) throw new Error('A profile revision is required.');
        const receipt = { id: mutation.id, revision };
        request = this.api.delete(mutation.id, revision, token, this.demoOwner(session)).pipe(map(() => receipt));
      } else {
        if (details === null) throw new Error('Profile fields are required.');
        if (mutation.kind === 'create') request = this.api.create(mutation.id, details, token, this.demoOwner(session));
        else {
          if (revision === null) throw new Error('A profile revision is required.');
          request = this.api.update(mutation.id, revision, details, token, this.demoOwner(session));
        }
      }
      details = null;
      this.dispatched = mutation;
      dispatched = true;
      const receipt = await firstValueFrom(request.pipe(takeUntil(this.cancelled)));
      if (!this.current(session)) return null;
      this.dispatched = null;
      this.mutationState.set('saved');
      this.message.set(mutation.kind === 'delete' ? 'Профиль удалён.' : 'Профиль сохранён.');
      this.items.set([]);
      this.hasMore.set(false);
      this.loadState.set('idle');
      return receipt;
    } catch (error) {
      details = null;
      if (!this.current(session)) return null;
      this.dispatched = null;
      const known = this.known(error);
      if (!dispatched) {
        this.mutationState.set('refused');
        this.authDenied();
      } else if (known !== null) {
        this.mutationState.set(
          known.status === 412
            ? 'conflict'
            : known.status === 404
              ? 'missing'
              : known.code === 'Flights.TravelerInvalid'
                ? 'validation'
                : 'refused',
        );
        this.fieldErrors.set(
          known.code === 'Flights.TravelerInvalid' && error instanceof HttpErrorResponse
            ? safeTravelerFieldErrors(error.error?.fieldErrors)
            : [],
        );
        this.message.set(
          known.status === 412
            ? 'Профиль изменился. Прочитайте актуальные данные и проверьте их перед новым действием.'
            : known.status === 404
              ? 'Профиль сейчас не виден. Не создавайте заново прежний идентификатор.'
              : 'Операция отклонена. Проверьте введённые данные и условия запроса.',
        );
        if (known.status === 401 || known.status === 403) this.authDenied();
      } else {
        this.rememberUnknown(mutation);
        this.mutationState.set('unknown');
        this.message.set(
          'Результат операции неизвестен. Не повторяйте отправку. Прочитайте текущее состояние профиля и проверьте его.',
        );
      }
      return null;
    }
  }
}
