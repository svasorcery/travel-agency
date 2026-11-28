import { isPlatformBrowser } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { computed, Injectable, Injector, inject, PLATFORM_ID, signal } from '@angular/core';
import {
  type TripBrief,
  type TripOptionPreview,
  type TripOptionSelection,
  TripsSavedOptionsApiService,
  tripBrief,
  tripText,
} from '@travel/api-client';
import { firstValueFrom, Subject, takeUntil } from 'rxjs';
import { TripsAuthService } from './trips-auth.service';
import { tripsProblemCode } from './trips-problem';

interface SaveOperation {
  owner: string;
  epoch: number;
  selection: TripOptionSelection;
  title?: string;
  brief: TripBrief;
  preview: TripOptionPreview;
}
@Injectable({ providedIn: 'root' })
export class TripsOptionSaveState {
  private readonly auth = inject(TripsAuthService);
  private readonly injector = inject(Injector);
  private readonly browser = isPlatformBrowser(inject(PLATFORM_ID));
  private readonly operation = signal<SaveOperation | null>(null);
  private readonly cancel = new Subject<void>();
  private readonly pending = signal<'saving' | 'checking' | 'uncertain'>('uncertain');
  private readonly unknownResult = signal(false);
  readonly message = signal('');
  readonly phase = computed(() => (this.current(this.operation()) ? this.pending() : null));
  readonly recovering = computed(() => !!this.phase() && this.unknownResult());

  private current(op: SaveOperation | null): op is SaveOperation {
    const owner = this.auth.status();
    return (
      !!op &&
      op === this.operation() &&
      owner.kind === 'authenticated' &&
      owner.userId === op.owner &&
      this.auth.identityEpoch() === op.epoch
    );
  }
  clear(): void {
    this.operation.set(null);
    this.unknownResult.set(false);
    this.cancel.next();
    this.message.set('');
  }
  async begin(selection: TripOptionSelection, title: string, brief: TripBrief, preview: TripOptionPreview) {
    const owner = this.auth.status();
    if (!this.browser || owner.kind !== 'authenticated' || this.phase()) return null;
    const normalizedTitle = title.trim();
    try {
      if (normalizedTitle) tripText(normalizedTitle, 100);
    } catch {
      this.message.set('Проверьте название поездки. Допустимо до 100 символов.');
      return null;
    }
    const op: SaveOperation = {
      owner: owner.userId,
      epoch: this.auth.identityEpoch(),
      selection,
      title: normalizedTitle || undefined,
      brief: tripBrief(brief),
      preview,
    };
    this.operation.set(op);
    return this.perform(op, 'save');
  }
  recover(check: boolean) {
    const op = this.operation();
    return this.current(op) && this.phase() === 'uncertain'
      ? this.perform(op, check ? 'check' : 'retry')
      : Promise.resolve(null);
  }
  private async perform(op: SaveOperation, action: 'save' | 'retry' | 'check'): Promise<string | null> {
    this.pending.set(action === 'check' ? 'checking' : 'saving');
    this.message.set(action === 'check' ? 'Проверяем сохранение…' : 'Сохраняем поездку…');
    try {
      const token = await this.auth.accessToken();
      if (!this.current(op)) return null;
      const api = this.injector.get(TripsSavedOptionsApiService);
      const id = op.selection.tripId;
      if (action === 'check') {
        const saved = await firstValueFrom(api.read(id, token).pipe(takeUntil(this.cancel)));
        if (!this.current(op)) return null;
        if (
          !saved ||
          JSON.stringify(saved.brief) !== JSON.stringify(op.brief) ||
          JSON.stringify(saved.preview) !== JSON.stringify(op.preview)
        ) {
          this.unknown();
          return null;
        }
      } else {
        await firstValueFrom(api.save(op.selection.selectionToken, op.title, id, token).pipe(takeUntil(this.cancel)));
        if (!this.current(op)) return null;
      }
      this.clear();
      return id;
    } catch (error) {
      if (!this.current(op)) return null;
      const status = error instanceof HttpErrorResponse ? error.status : 0;
      if (status === 401 || status === 403) {
        this.clear();
        this.auth.deny(op.epoch);
      } else if (action === 'save' && [400, 404, 409, 410, 413, 422, 429].includes(status)) {
        this.clear();
        this.message.set(
          tripsProblemCode(error) === 'Trips.CapacityExceeded'
            ? 'Достигнут предел сохранённых планов. Освободите место и подберите вариант снова.'
            : 'Сохранить этот вариант не удалось. Проверьте название и подберите вариант снова.',
        );
      } else this.unknown();
      return null;
    }
  }
  private unknown(): void {
    this.unknownResult.set(true);
    this.pending.set('uncertain');
    this.message.set(
      'Результат сохранения неизвестен. Исходный запрос ещё мог завершиться. Проверьте поездку или повторите то же сохранение.',
    );
  }
}
