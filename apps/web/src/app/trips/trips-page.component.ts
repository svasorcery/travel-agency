import { DOCUMENT } from '@angular/common';
import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  HostListener,
  Injector,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import {
  type TripBlock,
  type TripBrief,
  type TripCurrency,
  type TripInterest,
  type TripItineraryComputed,
  type TripItineraryEvidence,
  type TripPace,
  tripCurrencies,
  tripInterests,
  tripItineraryDataOffer,
  tripMoney,
  tripPaces,
  tripText,
} from '@travel/api-client';
import { RailSearchComponent } from '../rail/rail-search.component';
import type { RailSelection } from '../rail/rail-state';
import { TripsItineraryStateService } from './trips-itinerary-state.service';
import { projectRailIntention } from './trips-rail-projection';
import { type TripsSavedFoundation, TripsStateService } from './trips-state.service';

const labels: Record<TripInterest, string> = {
  architecture: 'Архитектура',
  culture: 'Культура',
  food: 'Еда',
  nature: 'Природа',
  history: 'История',
};
@Component({
  selector: 'app-trips-page',
  standalone: true,
  imports: [FormsModule, RouterLink, RailSearchComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './trips-page.component.html',
  styleUrl: './trips-page.component.scss',
})
export class TripsPageComponent {
  readonly state = inject(TripsStateService);
  readonly itineraryDataOffer = computed(() =>
    tripItineraryDataOffer(this.state.baseline()?.destination.destinationId ?? ''),
  );
  readonly itinerary = inject(TripsItineraryStateService);
  readonly auth = this.state.auth;
  readonly availability = this.state.availability;
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly document = inject(DOCUMENT);
  private readonly injector = inject(Injector);
  private readonly destroy = inject(DestroyRef);
  readonly currencies = tripCurrencies;
  readonly interests = tripInterests;
  readonly paces = tripPaces;
  readonly interestLabels = labels;
  readonly paceLabels: Record<TripPace, string> = { relaxed: 'Спокойный', balanced: 'Умеренный', active: 'Активный' };
  readonly deleteConfirmation = signal(false);
  private readonly railFoundation = signal<TripsSavedFoundation | null>(null);
  readonly railTargetDay = signal('');
  readonly railMessage = signal('');
  readonly railOpen = computed(() => {
    const foundation = this.railFoundation();
    return foundation !== null && this.state.isSavedFoundationCurrent(foundation);
  });
  // Key the single picker by the existing opaque ticket; reopening cannot retain its prior output binding.
  readonly railFoundations = computed(() => (this.railOpen() ? [this.railFoundation()!] : []));
  readonly railDays = computed(() => (this.railOpen() ? this.railFoundation()!.plan.days.map((day) => day.date) : []));
  readonly briefDraft = signal<TripBrief | null>(null);
  readonly errorField = signal<string | null>(null);
  readonly timeZone = computed(
    () =>
      this.state.baseline()?.destination.timeZone ??
      this.availability.catalogue()?.items.find((x) => x.destinationId === this.state.buffer()?.brief.destinationId)
        ?.timeZone ??
      'местное время',
  );
  readonly briefDirty = computed(() => {
    const current = this.state.buffer();
    return current !== null && JSON.stringify(current.brief) !== JSON.stringify(this.briefDraft());
  });
  readonly computedPreview = computed<TripItineraryComputed | null>(() => {
    const preview = this.itinerary.preview();
    return preview && preview.outcome !== 'unsupported' ? preview : null;
  });
  readonly dataFiles = computed(() => {
    const preview = this.computedPreview();
    if (!preview) return [];
    const offer = tripItineraryDataOffer(preview.destinationId);
    if (!offer || preview.derivedDataOffer !== offer) return [];
    const slash = offer.lastIndexOf('/'),
      directory = offer.slice(0, slash + 1),
      manifest = offer.slice(slash + 1);
    return [
      { name: 'Манифест пакета', path: manifest },
      { name: 'Данные мест и маршрутов', path: manifest.replace('.manifest.json', '.json') },
      { name: 'Основания пешей модели', path: 'walking-evidence.json' },
      { name: 'Входные данные воспроизведения', path: 'input-manifest.json' },
      { name: 'Состав пакета', path: 'inventory.json' },
      { name: 'Условия и указание авторства', path: 'ATTRIBUTION.txt' },
      ...(preview.destinationId === 'saint-petersburg'
        ? [{ name: 'Исключённые участки маршрутов', path: 'preparation-profile.json' }]
        : []),
    ].map((file) => ({ ...file, url: directory + file.path }));
  });
  private briefSource: TripBrief | null = null;
  private routeEpoch = 0;
  private focusIntent = 0;
  private destroyed = false;
  constructor() {
    this.route.paramMap.pipe(takeUntilDestroyed()).subscribe((params) => {
      this.deleteConfirmation.set(false);
      this.errorField.set(null);
      void this.enterPlan(params.get('id'));
    });
    effect(() => {
      const buffer = this.state.buffer();
      untracked(() => {
        if (buffer?.brief !== this.briefSource) {
          const prior = this.briefSource;
          this.briefSource = buffer?.brief ?? null;
          if (!buffer) this.briefDraft.set(null);
          else if (prior === null || JSON.stringify(prior) !== JSON.stringify(buffer.brief))
            this.briefDraft.set(structuredClone(buffer.brief));
        }
      });
    });
    effect(() => {
      const foundation = this.railFoundation();
      if (foundation && !this.state.isSavedFoundationCurrent(foundation))
        untracked(() => {
          this.railFoundation.set(null);
          this.railTargetDay.set('');
          this.railMessage.set(
            'Выбор поезда закрыт: состояние плана изменилось. Сохраните или сверьте план и откройте поиск снова.',
          );
        });
    });
    this.destroy.onDestroy(() => {
      this.railFoundation.set(null);
      this.destroyed = true;
      this.routeEpoch++;
      this.briefDraft.set(null);
      this.itinerary.leave();
      this.state.leave();
    });
  }
  openRail(): void {
    if (this.destroyed || this.briefDirty()) return;
    const foundation = this.state.captureSavedFoundation();
    if (!foundation) return;
    this.railFoundation.set(foundation);
    this.railTargetDay.set('');
    this.railMessage.set('');
    const intent = ++this.focusIntent;
    this.focus('rail-origin', intent, () => this.railOpen());
  }
  closeRail(): void {
    this.railFoundation.set(null);
    this.railTargetDay.set('');
    this.focus('open-rail');
  }
  chooseRail(selection: RailSelection, expectedFoundation: TripsSavedFoundation): void {
    const foundation = this.railFoundation(),
      date = this.railTargetDay();
    if (
      !foundation ||
      foundation !== expectedFoundation ||
      !this.state.isSavedFoundationCurrent(foundation) ||
      this.briefDirty() ||
      !selection.isCurrent()
    ) {
      this.railMessage.set('Выбор устарел. Откройте поиск снова после сверки плана.');
      return;
    }
    const day = foundation.plan.days.find((value) => value.date === date);
    if (!day) {
      this.railMessage.set('Выберите день плана для намерения. Дата отправления задаётся отдельно.');
      return;
    }
    if (day.blocks.length >= 8) {
      this.railMessage.set('В выбранном дне уже 8 блоков. Выберите другой день или измените план вручную.');
      return;
    }
    let block: TripBlock;
    try {
      block = projectRailIntention(selection);
    } catch {
      this.railMessage.set(
        'Сведения об этом рейсе не помещаются в заметку: до 500 символов. Выберите другой результат или напишите намерение вручную.',
      );
      return;
    }
    if (!selection.isCurrent() || !this.state.appendSavedFoundation(foundation, date, block, selection.isCurrent)) {
      this.railMessage.set(
        'Перенос не выполнен: основа изменилась или полный план превышает ограничения текста и 128 КиБ. Выберите другой результат или измените план вручную.',
      );
      return;
    }
    this.railFoundation.set(null);
    this.railTargetDay.set('');
    this.railMessage.set(
      'Вымышленное историческое намерение добавлено в редактор. Проверьте текст и отдельно нажмите «Сохранить».',
    );
    this.focus(`block-${block.blockId}`);
  }
  canLeave(): boolean {
    return (
      !(this.state.dirty() || this.briefDirty()) ||
      this.document.defaultView?.confirm('Есть несохранённые изменения. Покинуть план и удалить их из редактора?') ===
        true
    );
  }
  @HostListener('window:beforeunload', ['$event']) beforeUnload(event: BeforeUnloadEvent): void {
    if (this.state.dirty() || this.briefDirty()) {
      event.preventDefault();
      event.returnValue = '';
    }
  }
  async login(): Promise<void> {
    if (await this.auth.beginLogin(this.router.url)) await this.enterPlan(this.route.snapshot.paramMap.get('id'));
  }
  async logout(): Promise<void> {
    this.briefDraft.set(null);
    await this.state.logout();
  }
  patchBrief<K extends keyof TripBrief>(key: K, value: TripBrief[K]): void {
    const b = this.briefDraft();
    if (b) {
      const next = { ...b, [key]: value };
      this.state.touch(JSON.stringify(next) !== JSON.stringify(this.state.buffer()?.brief));
      this.briefDraft.set(next);
    }
  }
  toggleInterest(interest: TripInterest, checked: boolean): void {
    const b = this.briefDraft();
    if (b)
      this.patchBrief('interests', checked ? [...b.interests, interest] : b.interests.filter((x) => x !== interest));
  }
  applyBrief(): boolean {
    const b = this.briefDraft();
    if (!b) return false;
    const success = this.state.changeBrief(b);
    const current = this.state.buffer();
    const hasBlocks = current?.days.some((day) => day.blocks.length);
    const field =
      hasBlocks && b.destinationId !== current?.brief.destinationId
        ? 'trip-destination'
        : hasBlocks && b.currency !== current?.brief.currency
          ? 'trip-currency'
          : 'trip-start';
    this.errorField.set(success ? null : field);
    if (!success) this.focus(field);
    return success;
  }
  title(value: string): void {
    const d = this.state.buffer();
    if (d) this.state.edit({ ...d, title: value });
  }
  async submit(): Promise<void> {
    if (this.briefDirty() && !this.applyBrief()) return;
    if (this.state.selectedId()) await this.state.save();
    else await this.state.create();
    if (this.state.status() === 'invalid') {
      const field = this.validationTarget();
      this.errorField.set(field);
      this.focus(field);
    } else if (
      !this.state.dirty() &&
      !this.briefDirty() &&
      this.state.selectedId() &&
      this.route.snapshot.paramMap.get('id') !== this.state.selectedId()
    ) {
      await this.router.navigate(['/trips', this.state.selectedId()]);
    }
  }
  block(block: TripBlock, key: 'title' | 'note' | 'startTime' | 'endTime', value: string): void {
    this.state.updateBlock(block.blockId, { [key]: value === '' && key !== 'title' ? null : value });
  }
  estimate(block: TripBlock, value: string, currency: TripCurrency): void {
    this.state.updateBlock(block.blockId, { estimate: value === '' ? null : { amount: value, currency } });
  }
  addBlock(date: string): void {
    this.state.addBlock(date);
    const last = this.state
      .buffer()
      ?.days.find((x) => x.date === date)
      ?.blocks.at(-1);
    if (last) this.focus('block-' + last.blockId);
  }
  removeBlock(blockId: string, date: string): void {
    this.state.removeBlock(blockId);
    this.focus('add-' + date);
  }
  reorder(blockId: string, date: string, index: number): void {
    this.state.moveBlock(blockId, date, index);
    this.focus('block-' + blockId);
  }
  move(blockId: string, date: string): void {
    this.state.moveBlock(blockId, date, 8);
    this.focus('block-' + blockId);
  }
  async newPlan(): Promise<void> {
    if (!this.canLeave()) return;
    if (this.route.snapshot.paramMap.has('id')) await this.router.navigate(['/trips']);
    else {
      this.state.newPlan();
      await this.itinerary.activate();
    }
  }
  async deletePlan(): Promise<void> {
    await this.state.deletePlan();
    this.deleteConfirmation.set(false);
    if (this.state.selectedId() === null) await this.router.navigate(['/trips']);
  }
  resetBriefDraft(): void {
    const brief = this.state.buffer()?.brief ?? null;
    this.briefSource = brief;
    this.briefDraft.set(brief ? structuredClone(brief) : null);
    this.state.touch(false);
    this.errorField.set(null);
  }
  useCurrentServer(): void {
    this.state.useCurrentServer();
    this.resetBriefDraft();
  }
  async enterPlan(id: string | null): Promise<void> {
    this.focusIntent++;
    const epoch = ++this.routeEpoch;
    const identity = this.auth.identityEpoch();
    await this.state.enter(id);
    if (!this.destroyed && epoch === this.routeEpoch && identity === this.auth.identityEpoch())
      await this.itinerary.activate();
  }
  async refreshItineraryAvailability(): Promise<void> {
    const intent = ++this.focusIntent;
    const context = this.state.itineraryContext(),
      draft = this.state.buffer(),
      brief = this.briefDraft();
    await this.itinerary.refreshAvailability();
    const availability = this.itinerary.availability(),
      status = this.itinerary.status();
    const focusCurrent = () =>
      !this.destroyed &&
      context === this.state.itineraryContext() &&
      draft === this.state.buffer() &&
      brief === this.briefDraft() &&
      availability === this.itinerary.availability() &&
      status === this.itinerary.status() &&
      this.itinerary.pending() === null;
    if (
      intent === this.focusIntent &&
      focusCurrent() &&
      ['unavailable', 'unauthenticated', 'forbidden', 'disabled', 'unsupported'].includes(availability.kind)
    )
      this.focus('itinerary-status', intent, focusCurrent);
  }
  async generateItinerary(): Promise<void> {
    if (!this.itinerary.canGenerate()) return;
    const intent = ++this.focusIntent;
    const foundation = this.state.captureEmptyFoundation();
    await this.itinerary.generate();
    if (intent !== this.focusIntent || !foundation || !this.state.isEmptyFoundationCurrent(foundation)) return;
    const preview = this.itinerary.preview(),
      status = this.itinerary.status();
    const focusCurrent = () =>
      this.state.isEmptyFoundationCurrent(foundation) &&
      preview === this.itinerary.preview() &&
      status === this.itinerary.status() &&
      this.itinerary.pending() === null;
    if (preview) this.focus('itinerary-preview-title', intent, focusCurrent);
    else if (this.itinerary.pending() === null) this.focus('itinerary-status', intent, focusCurrent);
  }
  async adoptItinerary(): Promise<void> {
    if (!this.itinerary.canAdopt()) return;
    const intent = ++this.focusIntent;
    const focusCurrent = this.itinerary.captureAdoptionErrorFocusGuard();
    const adopted = await this.itinerary.adopt();
    if (intent !== this.focusIntent || this.destroyed) return;
    if (adopted) {
      const first = this.state.buffer()?.days.flatMap((day) => day.blocks)[0];
      if (first) this.focus(`block-${first.blockId}`, intent);
    } else if (focusCurrent && focusCurrent()) this.focus('itinerary-status', intent, focusCurrent);
  }
  dismissItinerary(): void {
    const intent = ++this.focusIntent;
    this.itinerary.dismiss();
    this.focus('generate-itinerary', intent);
  }
  itineraryMessage(): string {
    const availability = this.itinerary.availability().kind;
    const status = this.itinerary.status();
    const messages = {
      generating: 'Подбираем остановки. Редактор доступен; правки отменят это предложение.',
      'checking-adoption': 'Проверяем основу и пакет перед переносом. Редактор доступен.',
      candidate: 'Остановки заполнены. Выполнимость не проверена.',
      partial: 'Заполнено частично. Проверьте непокрытые пожелания и неизвестные.',
      empty: 'Подходящих остановок нет. Ручной редактор доступен.',
      unsupported: 'Основа не поддерживается для этого предложения.',
      staged:
        'Остановки перенесены в редактор как ваши намерения. Их можно изменить; для записи на сервер отдельно нажмите «Сохранить».',
      stale: 'Предложение устарело: основа или пакет изменились. Нужен новый явный расчёт.',
      expired: 'Срок предложения истёк. Перенос недоступен; нужен новый явный расчёт.',
      busy: 'Расчёт занят. Повторить можно отдельным действием позже.',
      deadline: 'Время расчёта истекло. Повторить можно отдельным действием.',
      conflict: 'Сначала сверьте план с сервером. Предложение не перенесено.',
      uncertain: 'Результат сохранения неизвестен. Сначала сверьте план с сервером.',
      disabled: 'Прогулочные предложения выключены. Ручной редактор доступен.',
      unauthenticated: 'Для предложений нужно подтвердить вход.',
      forbidden: 'Нет доступа к прогулочным предложениям.',
      unavailable: 'Предложение сейчас недоступно. Ручной редактор доступен.',
    };
    if (status !== 'idle') return messages[status] ?? '';
    return {
      unknown: 'Доступность предложений ещё не проверена.',
      checking: 'Проверяем доступность прогулочных предложений…',
      ready: 'Можно предложить остановки для сохранённой пустой основы.',
      disabled: messages.disabled,
      unauthenticated: messages.unauthenticated,
      forbidden: messages.forbidden,
      unavailable: messages.unavailable,
      unsupported: 'Для этого города пока нет прогулочных предложений. Ручной редактор доступен.',
    }[availability];
  }
  interestNames(values: string[]): string {
    return values.map((value) => labels[value as TripInterest] ?? value).join(', ') || 'Нет';
  }
  dayInterestCoverage(route: TripItineraryComputed, day: TripItineraryComputed['days'][number]) {
    const selected = new Set([...route.coveredInterests, ...route.unmetInterests]);
    const topics = new Set(day.blocks.flatMap((stop) => stop.tags));
    return {
      hasSelection: selected.size > 0,
      covered: tripInterests.filter((interest) => selected.has(interest) && topics.has(interest)),
      unmet: tripInterests.filter((interest) => selected.has(interest) && !topics.has(interest)),
    };
  }
  explanation(value: string): string {
    return (
      (
        {
          insufficientEligibleStops: 'Недостаточно подходящих остановок для выбранного темпа',
          unmetInterests: 'Часть пожеланий не покрыта',
          noEligibleStops: 'Нет подходящих остановок',
          searchLimited: 'Поиск остановлен на заданном пределе; это не доказательство отсутствия маршрута',
          arrivalScheduleNotPlanned: 'Расписание прибытия и отъезда не учтено',
          indoorReservationsNotPlanned: 'Входы в помещения и бронирования не спланированы',
          interdayTransfersNotPlanned: 'Переезды между днями не спланированы',
          totalCostUnknown: 'Полная стоимость неизвестна; соответствие бюджету не проверено',
          transferNotPlanned: 'Дорога из города отправления не спланирована',
          calibrationNotEvaluated: 'Калибровка пешей модели не проводилась',
          humanQualityNotEvaluated: 'Качество маршрута людьми не оценивалось',
          openingHoursUnknown: 'Часы доступа неизвестны',
          openingHoursExpiredOrNotApplicable: 'Нет актуальных часов доступа для даты поездки',
          walkingModelled: 'Пешие переходы рассчитаны по карте',
          destinationUnsupported: 'Для этого города пока нет прогулочных предложений',
          dateRangeUnsupported: 'Поддерживается поездка на 1–3 дня',
          historicalDatesUnsupported: 'Прошедшие даты не поддерживаются',
          horizonUnsupported: 'Даты вне поддерживаемого периода',
        } as Record<string, string>
      )[value] ?? value
    );
  }
  evidenceFor(preview: TripItineraryComputed, id: string | null): TripItineraryEvidence | undefined {
    return preview.evidence.find((evidence) => evidence.id === id);
  }
  distance(millimetres: string): string {
    return (Math.round(Number(millimetres) / 100000) / 10).toLocaleString('ru-RU', { maximumFractionDigits: 1 });
  }
  private validationTarget(): string {
    const d = this.state.buffer();
    if (!d) return 'trip-title';
    try {
      tripText(d.title, 100);
    } catch {
      return 'trip-title';
    }
    if (!Number.isInteger(d.brief.adults) || d.brief.adults < 1 || d.brief.adults > 9) return 'trip-adults';
    try {
      if (d.brief.totalBudget !== null) tripMoney(d.brief.totalBudget);
    } catch {
      return 'trip-budget';
    }
    for (const day of d.days)
      for (const b of day.blocks) {
        try {
          tripText(b.title, 100);
        } catch {
          return 'block-' + b.blockId;
        }
        try {
          if (b.note !== null) tripText(b.note, 500, true);
        } catch {
          return 'note-' + b.blockId;
        }
        if (
          (b.startTime === null) !== (b.endTime === null) ||
          (b.startTime !== null &&
            (!/^(?:[01]\d|2[0-3]):[0-5]\d$/.test(b.startTime) ||
              !/^(?:[01]\d|2[0-3]):[0-5]\d$/.test(b.endTime!) ||
              b.endTime! <= b.startTime))
        )
          return 'start-' + b.blockId;
        try {
          if (b.estimate !== null) tripMoney(b.estimate.amount);
        } catch {
          return 'estimate-' + b.blockId;
        }
      }
    return 'trip-title';
  }
  confirmDelete(): void {
    this.deleteConfirmation.set(true);
    this.focus('confirm-delete');
  }
  cancelDelete(): void {
    this.deleteConfirmation.set(false);
    this.focus('delete-plan');
  }
  private focus(id: string, intent = ++this.focusIntent, valid: () => boolean = () => true): void {
    if (intent !== this.focusIntent || this.destroyed) return;
    afterNextRender(
      () => {
        if (intent === this.focusIntent && !this.destroyed && valid()) this.document.getElementById(id)?.focus();
      },
      { injector: this.injector },
    );
  }
}
