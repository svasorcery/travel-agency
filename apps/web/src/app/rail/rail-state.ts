import type { RailRequest, RailSearchResult, RailStation, RailSuggestions } from '@travel/api-client';
export interface RailTransport {
  stations(q: string, signal: AbortSignal): Promise<RailSuggestions>;
  search(request: RailRequest, signal: AbortSignal): Promise<RailSearchResult>;
}
export class RailState {
  attributionFailed(): void {
    this.clearSource();
    this.message = 'Не удалось показать сведения об источнике. Данные очищены; повторите ввод вручную.';
    this.changed();
  }
  origin = { query: '', selected: null as RailStation | null, suggestions: [] as RailStation[], status: '' };
  destination = { query: '', selected: null as RailStation | null, suggestions: [] as RailStation[], status: '' };
  date = '';
  mode: 'train' | 'suburban' | 'all' = 'all';
  result: RailSearchResult | null = null;
  message = '';
  busy = false;
  reference: RailSuggestions | null = null;
  private fields = {
    origin: {
      epoch: 0,
      timer: null as ReturnType<typeof setTimeout> | null,
      controller: null as AbortController | null,
    },
    destination: {
      epoch: 0,
      timer: null as ReturnType<typeof setTimeout> | null,
      controller: null as AbortController | null,
    },
  };
  private sequence = 0;
  private authority = 0;
  private searchController: AbortController | null = null;
  private expiry: ReturnType<typeof setTimeout> | null = null;
  private disposed = false;
  constructor(
    private readonly api: RailTransport,
    private readonly changed: () => void = () => {},
  ) {}
  input(field: 'origin' | 'destination', q: string): void {
    this.criteriaChanged();
    const state = this.fields[field];
    state.epoch++;
    state.controller?.abort();
    if (state.timer !== null) clearTimeout(state.timer);
    this[field].query = q;
    this[field].selected = null;
    this[field].suggestions = [];
    this[field].status = '';
    if (q.length < 3 || q.length > 80) {
      this.changed();
      return;
    }
    const epoch = state.epoch;
    const authority = this.authority;
    state.controller = new AbortController();
    const signal = state.controller.signal;
    state.timer = setTimeout(async () => {
      state.timer = null;
      this[field].status = 'Загружаем станции…';
      this.changed();
      try {
        const reply = await this.api.stations(q, signal);
        if (this.disposed || signal.aborted || epoch !== state.epoch) return;
        if (reply.kind === 'failed' || reply.kind === 'skipped') {
          if (authority !== this.authority && this.reference?.profileEpoch !== reply.profileEpoch) return;
          this[field].status =
            reply.kind === 'failed'
              ? 'Станции недоступны: ошибка источника.'
              : 'Источник пока выключен или ограничен квотой.';
          if (reply.reasons.includes('profile-changed') || this.reference?.profileEpoch !== reply.profileEpoch)
            this.clearSource();
          return;
        }
        if (Date.parse(reply.expiresAt!) <= Date.now()) return;
        const differs =
          this.reference !== null &&
          (this.reference.catalogueGeneration !== reply.catalogueGeneration ||
            this.reference.profileEpoch !== reply.profileEpoch ||
            this.reference.profile !== reply.profile);
        if (differs && authority !== this.authority) return;
        if (this.reference === null || differs) {
          if (differs) {
            this.clearSelections();
            this.message = 'Каталог изменился. Выберите обе станции заново.';
          }
          this.authority++;
        }
        this.reference = { ...reply, suggestions: [] };
        this[field].suggestions = reply.suggestions;
        this[field].status =
          reply.kind === 'limited'
            ? 'Показана ограниченная часть станций.'
            : reply.suggestions.length === 0
              ? 'Совпадений не найдено.'
              : 'Выберите отдельную станцию.';
        this.scheduleExpiry();
      } catch {
        if (!signal.aborted && epoch === state.epoch)
          this[field].status = 'Не удалось прочитать станции. Повторите ввод.';
      } finally {
        this.changed();
      }
    }, 300);
    this.changed();
  }
  select(field: 'origin' | 'destination', station: RailStation): void {
    if (
      !this.reference ||
      Date.parse(this.reference.expiresAt!) <= Date.now() ||
      !this[field].suggestions.some((x) => x.reference.code === station.reference.code)
    )
      return;
    this.criteriaChanged();
    this[field].selected = station;
    this[field].query = station.name;
    this[field].suggestions = [];
    this[field].status = 'Станция выбрана.';
    this.changed();
  }
  criteriaChanged(): void {
    this.sequence++;
    this.searchController?.abort();
    this.searchController = null;
    this.result = null;
    this.busy = false;
    this.message = '';
    this.scheduleExpiry();
  }
  async search(): Promise<void> {
    this.criteriaChanged();
    const reference = this.reference;
    const from = this.origin.selected;
    const to = this.destination.selected;
    if (
      !reference ||
      !from ||
      !to ||
      from.reference.code === to.reference.code ||
      !this.date ||
      Date.parse(reference.expiresAt!) <= Date.now()
    ) {
      this.message = 'Выберите две разные станции и дату отправления.';
      this.changed();
      return;
    }
    const request: RailRequest = {
      from: from.reference,
      to: to.reference,
      catalogueGeneration: reference.catalogueGeneration!,
      departureDate: this.date,
      transport: this.mode,
    };
    const sequence = this.sequence;
    this.searchController = new AbortController();
    const signal = this.searchController.signal;
    this.busy = true;
    this.changed();
    try {
      const reply = await this.api.search(request, signal);
      if (this.disposed || signal.aborted || sequence !== this.sequence) return;
      if (
        reply.profile !== reference.profile ||
        reply.profileEpoch !== reference.profileEpoch ||
        reply.reasons.includes('profile-changed')
      ) {
        this.clearSource();
        this.message = 'Источник изменился. Выберите станции заново.';
        return;
      }
      if (reply.expiresAt !== null && Date.parse(reply.expiresAt) <= Date.now()) {
        this.message = 'Срок данных истёк. Обновите поиск вручную.';
        return;
      }
      this.result = reply;
      this.message =
        reply.kind === 'complete'
          ? reply.items.length === 0
            ? 'Поездок в выбранном расписании не найдено.'
            : 'Расписание получено.'
          : reply.kind === 'limited'
            ? 'Получена ограниченная часть расписания; отсутствие строк не означает отсутствия поездок.'
            : reply.kind === 'skipped'
              ? 'Поиск не запущен: источник выключен или ограничен квотой.'
              : 'Не удалось получить расписание. Это не отсутствие поездов.';
      this.scheduleExpiry();
    } catch (error) {
      if (
        error &&
        typeof error === 'object' &&
        'code' in error &&
        error.code === 'Rail.Profile.Changed' &&
        !signal.aborted &&
        sequence === this.sequence
      ) {
        this.clearSource();
        this.message = 'Источник изменился. Выберите станции заново.';
        return;
      }
      if (!signal.aborted && sequence === this.sequence) {
        this.result = null;
        this.message = 'Не удалось прочитать поиск. Повторите вручную.';
        if (
          error &&
          typeof error === 'object' &&
          'error' in error &&
          JSON.stringify(error.error).includes('Rail.StationSelection.Expired')
        ) {
          this.clearSource();
          this.message = 'Каталог истёк. Выберите обе станции заново.';
        }
      }
    } finally {
      if (sequence === this.sequence) this.busy = false;
      this.changed();
    }
  }
  private clearSelections(): void {
    for (const field of [this.origin, this.destination])
      if (field.selected) {
        field.query = '';
        field.status = 'Выберите станцию заново.';
      }
    this.origin.selected = null;
    this.destination.selected = null;
    this.origin.suggestions = [];
    this.destination.suggestions = [];
    this.criteriaChanged();
  }
  private clearSource(): void {
    for (const field of Object.values(this.fields)) {
      field.epoch++;
      field.controller?.abort();
      if (field.timer !== null) clearTimeout(field.timer);
      field.timer = null;
    }
    this.clearSelections();
    this.reference = null;
    this.result = null;
    this.authority++;
    this.scheduleExpiry();
  }
  private scheduleExpiry(): void {
    if (this.expiry !== null) clearTimeout(this.expiry);
    this.expiry = null;
    if (this.disposed) return;
    const dates = [this.reference?.expiresAt, this.result?.expiresAt]
      .filter((x): x is string => typeof x === 'string')
      .map(Date.parse);
    if (dates.length === 0) return;
    this.expiry = setTimeout(
      () => {
        if (this.reference && Date.parse(this.reference.expiresAt!) <= Date.now()) {
          this.clearSource();
          this.message = 'Каталог истёк. Выберите обе станции заново.';
        } else if (this.result?.expiresAt && Date.parse(this.result.expiresAt) <= Date.now()) {
          this.result = null;
          this.message = 'Срок расписания и цен истёк. Обновите поиск вручную.';
          this.scheduleExpiry();
        }
        this.changed();
      },
      Math.max(0, Math.min(...dates) - Date.now()),
    );
  }
  dispose(): void {
    this.disposed = true;
    this.sequence++;
    this.searchController?.abort();
    for (const state of Object.values(this.fields)) {
      state.controller?.abort();
      if (state.timer !== null) clearTimeout(state.timer);
    }
    if (this.expiry !== null) clearTimeout(this.expiry);
    this.clearSource();
  }
}
