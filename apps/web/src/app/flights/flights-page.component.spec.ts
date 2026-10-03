import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter, Router } from '@angular/router';
import type { FlightQuoteResponse } from '@travel/api-client';
// Shared canonical HTTP fixture, test only.
// eslint-disable-next-line @nx/enforce-module-boundaries
import booking from '../../../../../tests/fixtures/flights-booking.json';
// Shared canonical HTTP fixture, test only.
// eslint-disable-next-line @nx/enforce-module-boundaries
import fixtures from '../../../../../tests/fixtures/flights-search.json';
import { FlightsBookingPanelComponent } from './flight-booking-panel.component';
import { FlightOrderOperationsService } from './flight-order-operations.service';
import { FlightsAuthService, type FlightsAuthStatus } from './flights-auth.service';
import { FlightsPageComponent } from './flights-page.component';

describe('FlightsPageComponent', () => {
  let http: HttpTestingController;
  let authStub: {
    isDemo: boolean;
    isTestEnvironment: ReturnType<typeof vi.fn>;
    status: ReturnType<typeof signal<FlightsAuthStatus>>;
    hasOidcCallback: ReturnType<typeof vi.fn>;
    initializeFromCallback: ReturnType<typeof vi.fn>;
    beginLogin: ReturnType<typeof vi.fn>;
    accessToken: ReturnType<typeof vi.fn>;
    logout: ReturnType<typeof vi.fn>;
  };

  beforeEach(async () => {
    authStub = {
      isDemo: false,
      isTestEnvironment: vi.fn().mockReturnValue(false),
      status: signal<FlightsAuthStatus>({ kind: 'anonymous' }),
      hasOidcCallback: vi.fn().mockReturnValue(false),
      initializeFromCallback: vi.fn().mockResolvedValue(false),
      beginLogin: vi.fn().mockResolvedValue(false),
      accessToken: vi.fn().mockResolvedValue(null),
      logout: vi.fn().mockResolvedValue(undefined),
    };
    await TestBed.configureTestingModule({
      imports: [FlightsPageComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: FlightsAuthService, useValue: authStub },
        provideRouter([]),
      ],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  });

  afterEach(() => {
    http.verify();
    vi.useRealTimers();
  });

  function createPage() {
    const fixture = TestBed.createComponent(FlightsPageComponent);
    fixture.detectChanges();
    return { fixture, page: fixture.componentInstance, root: fixture.nativeElement as HTMLElement };
  }

  function fillValid(page: FlightsPageComponent) {
    page.form.patchValue({
      origin: fixtures.oneWay.request.origin,
      destination: fixtures.oneWay.request.destination,
      departureDate: fixtures.oneWay.request.departureDate,
    });
  }

  function submit(root: HTMLElement) {
    (root.querySelector('button[type="submit"]') as HTMLButtonElement).click();
  }

  it('starts idle and never searches before submit', () => {
    const { root } = createPage();
    expect(root.textContent).toContain('Найти рейсы');
    http.expectNone('/api/flights/search?currency=RUB');
  });

  it('shows field guidance and sends no request for invalid input', async () => {
    const { fixture, root } = createPage();
    submit(root);
    fixture.detectChanges();
    expect(root.textContent).toContain('Введите код аэропорта');
    expect(root.querySelector('[aria-invalid="true"]')).not.toBeNull();
    http.expectNone('/api/flights/search?currency=RUB');
  });

  it('shows loading, both offer kinds, and a partner limitation', () => {
    const { fixture, page, root } = createPage();
    fillValid(page);
    submit(root);
    fixture.detectChanges();
    expect(root.textContent).toContain('Ищем рейсы');
    expect((root.querySelector('button[type="submit"]') as HTMLButtonElement).disabled).toBe(true);
    const request = http.expectOne('/api/flights/search?currency=RUB');
    submit(root);
    http.expectNone('/api/flights/search?currency=RUB');
    request.flush(fixtures.oneWay.response);
    fixture.detectChanges();
    expect(root.textContent).toContain('SU101');
    expect(root.textContent).toContain('Example Partner');
    expect(root.textContent).toContain('Детали маршрута уточняются у партнёра');
    expect(root.querySelector('a[href="https://partner.invalid/fixture"]')).toBeNull();
  });

  it('keeps empty and partial-provider warnings separate', () => {
    const { fixture, page, root } = createPage();
    fillValid(page);
    submit(root);
    http.expectOne('/api/flights/search?currency=RUB').flush(fixtures.emptyPartial.response);
    fixture.detectChanges();
    expect(root.textContent).toContain('предложений не найдено');
    expect(root.textContent).toContain('Часть источников не ответила');
  });

  it('warns about a different currency without claiming a provider failure', () => {
    const { fixture, page, root } = createPage();
    fillValid(page);
    submit(root);
    http.expectOne('/api/flights/search?currency=RUB').flush(fixtures.mixedCurrency.response);
    fixture.detectChanges();
    expect(root.textContent).toContain('другой валюте');
    expect(root.textContent).toContain('EUR');
    expect(root.textContent).not.toContain('Часть источников не ответила');
  });

  it('shows offers and a partial-source warning together', () => {
    const { fixture, page, root } = createPage();
    fillValid(page);
    submit(root);
    http.expectOne('/api/flights/search?currency=RUB').flush(fixtures.partial.response);
    fixture.detectChanges();
    expect(root.textContent).toContain('SU101');
    expect(root.textContent).toContain('Часть источников не ответила');
  });

  it.each([
    [400, 'Проверьте условия поиска'],
    [401, 'настройки доступа'],
    [503, 'временно недоступен'],
  ])('distinguishes HTTP %i without losing the form', (status, message) => {
    const { fixture, page, root } = createPage();
    fillValid(page);
    submit(root);
    http.expectOne('/api/flights/search?currency=RUB').flush({ status }, { status, statusText: 'Error' });
    fixture.detectChanges();
    expect(root.textContent).toContain(message);
    expect(page.form.controls.origin.value).toBe('LED');
  });

  it('reports malformed success payload as a contract error', () => {
    const { fixture, page, root } = createPage();
    fillValid(page);
    submit(root);
    http.expectOne('/api/flights/search?currency=RUB').flush({ offers: [{}], partialFailures: [] });
    fixture.detectChanges();
    expect(root.textContent).toContain('Не удалось прочитать результаты');
  });

  it('reports syntactically invalid JSON from HTTP 200 as an unreadable result', () => {
    const { fixture, page, root } = createPage();
    fillValid(page);
    submit(root);
    http.expectOne('/api/flights/search?currency=RUB').flush('not json', {
      status: 200,
      statusText: 'OK',
    });
    fixture.detectChanges();
    expect(root.textContent).toContain('Не удалось прочитать результаты');
  });

  it('reports a network failure separately from an empty result', () => {
    const { fixture, page, root } = createPage();
    fillValid(page);
    submit(root);
    http.expectOne('/api/flights/search?currency=RUB').error(new ProgressEvent('error'));
    fixture.detectChanges();
    expect(root.textContent).toContain('Нет соединения с поиском');
    expect(root.textContent).not.toContain('предложений не найдено');
  });

  it('retains criteria and offers a manual retry after provider failure', () => {
    const { fixture, page, root } = createPage();
    fillValid(page);
    submit(root);
    http.expectOne('/api/flights/search?currency=RUB').flush(fixtures.unavailable.response, {
      status: 500,
      statusText: 'Internal Server Error',
    });
    fixture.detectChanges();
    expect(root.textContent).toContain('временно недоступен');
    expect(page.form.controls.origin.value).toBe('LED');
    (root.querySelector('button[data-action="retry"]') as HTMLButtonElement).click();
    http.expectOne('/api/flights/search?currency=RUB').flush(fixtures.oneWay.response);
    fixture.detectChanges();
    expect(root.textContent).toContain('SU101');
  });

  it('cancels an old response after criteria change', () => {
    const { fixture, page, root } = createPage();
    fillValid(page);
    submit(root);
    const old = http.expectOne('/api/flights/search?currency=RUB');
    page.form.controls.destination.setValue('VKO');
    expect(old.cancelled).toBe(true);
    fixture.detectChanges();
    expect(root.textContent).not.toContain('Ищем рейсы');
    submit(root);
    const latest = http.expectOne('/api/flights/search?currency=RUB');
    latest.flush(fixtures.empty.response);
    fixture.detectChanges();
    expect(root.textContent).toContain('предложений не найдено');
  });

  it('cancels an in-flight search when the page is destroyed', () => {
    const { fixture, page, root } = createPage();
    fillValid(page);
    submit(root);
    const request = http.expectOne('/api/flights/search?currency=RUB');
    fixture.destroy();
    expect(request.cancelled).toBe(true);
  });

  it('shows a bounded timeout and permits a later manual retry', () => {
    vi.useFakeTimers();
    const { fixture, page, root } = createPage();
    fillValid(page);
    submit(root);
    const request = http.expectOne('/api/flights/search?currency=RUB');
    vi.advanceTimersByTime(15_001);
    fixture.detectChanges();
    expect(request.cancelled).toBe(true);
    expect(root.textContent).toContain('Поиск занял слишком много времени');
    (root.querySelector('button[data-action="retry"]') as HTMLButtonElement).click();
    http.expectOne('/api/flights/search?currency=RUB').flush(fixtures.empty.response);
    fixture.detectChanges();
    expect(root.textContent).toContain('предложений не найдено');
  });

  it('quotes only a bookable offer and requires acceptance of the new search price', () => {
    const { fixture, page, root } = createPage();
    fillValid(page);
    submit(root);
    http.expectOne('/api/flights/search?currency=RUB').flush(fixtures.oneWay.response);
    fixture.detectChanges();
    const actions = root.querySelectorAll<HTMLButtonElement>('button[data-action="quote"]');
    expect(actions).toHaveLength(1);
    actions[0].click();
    fixture.detectChanges();
    expect(root.textContent).toContain('Проверяем актуальную цену');
    const request = http.expectOne('/api/flights/orders/quote');
    expect(request.request.body).toEqual(booking.oneWay.request);
    expect(request.request.headers.has('Authorization')).toBe(false);
    actions[0].click();
    http.expectNone('/api/flights/orders/quote');
    request.flush(booking.oneWay.response);
    fixture.detectChanges();
    expect(root.textContent).toContain('10 800 RUB');
    expect(root.querySelector('.quote-panel')?.textContent).toContain('Цена после проверки');
    expect(root.textContent).toContain('Цена или маршрут изменились');
    expect(root.querySelector('.quote-panel__notice')?.textContent).toContain('В поиске: 10 500 RUB');
    expect(root.querySelector('.quote-panel__notice')?.textContent).toContain('После проверки: 10 800 RUB');
    expect(root.textContent).toContain('Изменение допускается');
    expect(root.textContent).toContain('Возврат не предусмотрен');
    expect(root.textContent).toContain('Мест зарегистрированного багажа (максимум на пассажира и сегмент): 1');
    expect(root.textContent).toContain('Нормы могут различаться по сегментам');
    (root.querySelector('button[data-action="accept-quote"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(root.textContent).toContain('Актуальное предложение принято');
  });

  it('re-quotes the same aggregate and clears quote when search criteria change', () => {
    const { fixture, page, root } = createPage();
    fillValid(page);
    submit(root);
    http.expectOne('/api/flights/search?currency=RUB').flush(fixtures.oneWay.response);
    fixture.detectChanges();
    (root.querySelector('button[data-action="quote"]') as HTMLButtonElement).click();
    http.expectOne('/api/flights/orders/quote').flush(booking.oneWay.response);
    fixture.detectChanges();
    (root.querySelector('button[data-action="requote"]') as HTMLButtonElement).click();
    const request = http.expectOne('/api/flights/orders/quote');
    expect(request.request.body).toEqual(booking.reQuoteChanged.request);
    page.form.controls.destination.setValue('VKO');
    expect(request.cancelled).toBe(true);
    fixture.detectChanges();
    expect(root.textContent).not.toContain('Актуальная цена');
  });

  it('shows an unknown quote outcome and never retries automatically', () => {
    const { fixture, page, root } = createPage();
    fillValid(page);
    submit(root);
    http.expectOne('/api/flights/search?currency=RUB').flush(fixtures.oneWay.response);
    fixture.detectChanges();
    (root.querySelector('button[data-action="quote"]') as HTMLButtonElement).click();
    http.expectOne('/api/flights/orders/quote').error(new ProgressEvent('error'));
    fixture.detectChanges();
    expect(root.textContent).toContain('Ответ на проверку цены не получен');
    http.expectNone('/api/flights/orders/quote');
    (root.querySelector('button[data-action="retry-quote"]') as HTMLButtonElement).click();
    http.expectOne('/api/flights/orders/quote').flush(booking.oneWay.response);
    fixture.detectChanges();
    expect(root.textContent).toContain('Актуальная цена');
  });

  it('shows both verified directions for a round-trip quote', () => {
    const { fixture, page, root } = createPage();
    page.form.patchValue({
      tripType: 'roundTrip',
      origin: 'LED',
      destination: 'DME',
      departureDate: '2030-06-10',
      returnDate: '2030-06-17',
    });
    submit(root);
    http.expectOne('/api/flights/search?currency=RUB').flush(fixtures.roundTrip.response);
    fixture.detectChanges();
    (root.querySelector('button[data-action="quote"]') as HTMLButtonElement).click();
    const request = http.expectOne('/api/flights/orders/quote');
    expect(request.request.body).toEqual(booking.roundTrip.request);
    request.flush(booking.roundTrip.response);
    fixture.detectChanges();
    expect(root.querySelectorAll('.quote-panel .offer__slice')).toHaveLength(2);
    expect(root.querySelector('.quote-panel')?.textContent).toContain('SU102');
  });

  it('does not offer acceptance for an expired quote and re-quotes its aggregate', () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2030-06-09T23:59:01Z'));
    const { fixture, page, root } = createPage();
    fillValid(page);
    submit(root);
    http.expectOne('/api/flights/search?currency=RUB').flush(fixtures.oneWay.response);
    fixture.detectChanges();
    (root.querySelector('button[data-action="quote"]') as HTMLButtonElement).click();
    http.expectOne('/api/flights/orders/quote').flush(booking.oneWay.response);
    fixture.detectChanges();
    expect(root.textContent).toContain('Срок проверенного предложения истёк');
    expect(root.querySelector('button[data-action="accept-quote"]')).toBeNull();
    (root.querySelector('button[data-action="requote"]') as HTMLButtonElement).click();
    expect(http.expectOne('/api/flights/orders/quote').request.body).toEqual(booking.reQuoteChanged.request);
  });

  it('distinguishes unavailable and malformed quote responses', () => {
    const { fixture, page, root } = createPage();
    fillValid(page);
    submit(root);
    http.expectOne('/api/flights/search?currency=RUB').flush(fixtures.oneWay.response);
    fixture.detectChanges();
    (root.querySelector('button[data-action="quote"]') as HTMLButtonElement).click();
    http.expectOne('/api/flights/orders/quote').flush({ status: 404 }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();
    expect(root.textContent).toContain('больше недоступно');
    expect(root.textContent).toContain('попробуйте проверить ещё раз');
    (root.querySelector('button[data-action="retry-quote"]') as HTMLButtonElement).click();
    http.expectOne('/api/flights/orders/quote').flush({ aggregateId: 'bad' });
    fixture.detectChanges();
    expect(root.textContent).toContain('Не удалось прочитать проверенное предложение');
  });

  it('describes a quote concurrency conflict as a known conflict', () => {
    const { fixture, page, root } = createPage();
    fillValid(page);
    submit(root);
    http.expectOne('/api/flights/search?currency=RUB').flush(fixtures.oneWay.response);
    fixture.detectChanges();
    (root.querySelector('button[data-action="quote"]') as HTMLButtonElement).click();
    http.expectOne('/api/flights/orders/quote').flush(booking.oneWay.response);
    fixture.detectChanges();
    (root.querySelector('button[data-action="requote"]') as HTMLButtonElement).click();
    http.expectOne('/api/flights/orders/quote').flush({ status: 409 }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();
    expect(root.textContent).toContain('конфликт состояния');
    expect(root.textContent).not.toContain('изменилось одновременно');
    expect(root.textContent).not.toContain('Исход запроса неизвестен');
  });

  it('does not accept a quote for a different provider reference', () => {
    const { fixture, page, root } = createPage();
    fillValid(page);
    submit(root);
    http.expectOne('/api/flights/search?currency=RUB').flush(fixtures.oneWay.response);
    fixture.detectChanges();
    (root.querySelector('button[data-action="quote"]') as HTMLButtonElement).click();
    http.expectOne('/api/flights/orders/quote').flush({
      ...booking.oneWay.response,
      offer: { ...booking.oneWay.response.offer, providerOfferRef: 'off_fixture_other' },
    });
    fixture.detectChanges();
    expect(root.textContent).toContain('Не удалось прочитать проверенное предложение');
    expect(root.querySelector('button[data-action="accept-quote"]')).toBeNull();
  });

  it('rejects a re-quote response that switches aggregateId', () => {
    const { fixture, page, root } = createPage();
    fillValid(page);
    submit(root);
    http.expectOne('/api/flights/search?currency=RUB').flush(fixtures.oneWay.response);
    fixture.detectChanges();
    (root.querySelector('button[data-action="quote"]') as HTMLButtonElement).click();
    http.expectOne('/api/flights/orders/quote').flush(booking.oneWay.response);
    fixture.detectChanges();
    (root.querySelector('button[data-action="requote"]') as HTMLButtonElement).click();
    http.expectOne('/api/flights/orders/quote').flush({
      ...booking.reQuoteChanged.response,
      aggregateId: booking.roundTrip.response.aggregateId,
    });
    fixture.detectChanges();
    expect(root.textContent).toContain('Не удалось прочитать проверенное предложение');
    expect(root.querySelector('button[data-action="accept-quote"]')).toBeNull();
  });

  it('calls a failed re-quote retry an update and keeps its aggregateId', () => {
    const { fixture, page, root } = createPage();
    fillValid(page);
    submit(root);
    http.expectOne('/api/flights/search?currency=RUB').flush(fixtures.oneWay.response);
    fixture.detectChanges();
    (root.querySelector('button[data-action="quote"]') as HTMLButtonElement).click();
    http.expectOne('/api/flights/orders/quote').flush(booking.oneWay.response);
    fixture.detectChanges();
    (root.querySelector('button[data-action="requote"]') as HTMLButtonElement).click();
    http.expectOne('/api/flights/orders/quote').flush({ status: 503 }, { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges();
    const retry = root.querySelector('button[data-action="retry-quote"]') as HTMLButtonElement;
    expect(retry.textContent).toContain('Повторить обновление');
    expect(root.querySelector('.quote-panel__notice')?.textContent).not.toContain('новую проверку');
    retry.click();
    expect(http.expectOne('/api/flights/orders/quote').request.body.aggregateId).toBe(
      booking.oneWay.response.aggregateId,
    );
  });

  it('shows the previous quoted price when a re-quote changes it again', () => {
    const { fixture, page, root } = createPage();
    fillValid(page);
    submit(root);
    http.expectOne('/api/flights/search?currency=RUB').flush(fixtures.oneWay.response);
    fixture.detectChanges();
    (root.querySelector('button[data-action="quote"]') as HTMLButtonElement).click();
    http.expectOne('/api/flights/orders/quote').flush(booking.oneWay.response);
    fixture.detectChanges();
    (root.querySelector('button[data-action="requote"]') as HTMLButtonElement).click();
    http.expectOne('/api/flights/orders/quote').flush(booking.reQuoteChanged.response);
    fixture.detectChanges();
    expect(root.querySelector('.quote-panel__notice')?.textContent).toContain('Предыдущая проверка: 10 800 RUB');
    expect(root.querySelector('.quote-panel__notice')?.textContent).toContain('После проверки: 10 900 RUB');
  });

  it('shows the searched and quoted itineraries together before accepting a route change', () => {
    const { fixture, page, root } = createPage();
    fillValid(page);
    submit(root);
    http.expectOne('/api/flights/search?currency=RUB').flush(fixtures.oneWay.response);
    fixture.detectChanges();
    (root.querySelector('button[data-action="quote"]') as HTMLButtonElement).click();
    const quote = booking.oneWay.response;
    http.expectOne('/api/flights/orders/quote').flush({
      ...quote,
      offer: {
        ...quote.offer,
        totalAmount: 10500,
        itinerary: {
          ...quote.offer.itinerary,
          slices: [
            {
              ...quote.offer.itinerary.slices[0],
              segments: [{ ...quote.offer.itinerary.slices[0].segments[0], flightNumber: 'SU999' }],
            },
          ],
        },
      },
    });
    fixture.detectChanges();
    const panel = root.querySelector('.quote-panel') as HTMLElement;
    expect(panel.textContent).toContain('Маршрут в поиске');
    expect(panel.textContent).toContain('SU101');
    expect(panel.textContent).toContain('SU999');
    expect(panel.querySelector('button[data-action="accept-quote"]')).not.toBeNull();
  });

  it('does not accept a quote that expired between clock updates', () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2030-06-09T23:58:59Z'));
    const { fixture, page, root } = createPage();
    fillValid(page);
    submit(root);
    http.expectOne('/api/flights/search?currency=RUB').flush(fixtures.oneWay.response);
    fixture.detectChanges();
    (root.querySelector('button[data-action="quote"]') as HTMLButtonElement).click();
    http.expectOne('/api/flights/orders/quote').flush(booking.oneWay.response);
    fixture.detectChanges();
    vi.setSystemTime(new Date('2030-06-09T23:59:01Z'));
    (root.querySelector('button[data-action="accept-quote"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(root.textContent).not.toContain('Актуальное предложение принято');
    expect(root.textContent).toContain('Срок проверенного предложения истёк');
  });

  it('shows expiry as soon as the quote deadline passes', () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2030-06-09T23:58:59Z'));
    const { fixture, page, root } = createPage();
    fillValid(page);
    submit(root);
    http.expectOne('/api/flights/search?currency=RUB').flush(fixtures.oneWay.response);
    fixture.detectChanges();
    (root.querySelector('button[data-action="quote"]') as HTMLButtonElement).click();
    http.expectOne('/api/flights/orders/quote').flush(booking.oneWay.response);
    fixture.detectChanges();
    expect(root.querySelector('button[data-action="accept-quote"]')).not.toBeNull();
    vi.advanceTimersByTime(1_500);
    fixture.detectChanges();
    expect(root.textContent).toContain('Срок проверенного предложения истёк');
    expect(root.querySelector('button[data-action="accept-quote"]')).toBeNull();
  });

  it('deletes the legacy draft without reading it and requires selection after callback', async () => {
    const storage = window.sessionStorage;
    storage.setItem('travel.flights.booking.v1', 'old-private-intent');
    storage.setItem('unrelated', 'keep');
    const read = vi.spyOn(Storage.prototype, 'getItem');
    const write = vi.spyOn(Storage.prototype, 'setItem');
    authStub.status.set({ kind: 'authenticated', userId: 'bfb631f9-c340-4f2a-bcd7-9427d17d8c59' });
    authStub.hasOidcCallback.mockReturnValue(true);
    authStub.initializeFromCallback.mockResolvedValue(true);
    const { fixture, page, root } = createPage();
    await fixture.whenStable();
    fixture.detectChanges();
    http.expectNone(() => true);
    expect(read).not.toHaveBeenCalled();
    expect(write).not.toHaveBeenCalled();
    read.mockRestore();
    write.mockRestore();
    expect(storage.getItem('travel.flights.booking.v1')).toBeNull();
    expect(storage.getItem('unrelated')).toBe('keep');
    storage.removeItem('unrelated');
    expect(page.quoteState().kind).toBe('idle');
    expect(root.textContent).toContain('Вход выполнен. Выберите рейс и проверьте цену заново.');
    expect(root.querySelector('app-flight-booking-panel')).toBeNull();
  });

  it('starts login even when browser storage is unavailable and persists no intent', async () => {
    const { fixture, page, root } = createPage();
    fillValid(page);
    submit(root);
    http.expectOne('/api/flights/search?currency=RUB').flush(fixtures.oneWay.response);
    page.checkOffer(fixtures.oneWay.response.offers[0].id);
    http.expectOne('/api/flights/orders/quote').flush(booking.oneWay.response);
    const storage = vi.spyOn(window, 'sessionStorage', 'get').mockImplementation(() => {
      throw new Error('Storage unavailable');
    });
    authStub.beginLogin.mockImplementation(async () => {
      authStub.status.set({ kind: 'redirecting' });
      return false;
    });
    page.acceptQuote();
    await page.beginBooking();
    storage.mockRestore();
    fixture.detectChanges();
    expect(authStub.beginLogin).toHaveBeenCalledOnce();
    expect(root.textContent).toContain('После входа выберите рейс и проверьте цену заново');
    http.expectNone(() => true);
  });

  it('does not allocate a new quote aggregate after criteria changes while a hold is unknown', async () => {
    authStub.status.set({ kind: 'authenticated', userId: 'owner-one' });
    const operations = TestBed.inject(FlightOrderOperationsService);
    operations.startHold(
      {
        aggregateId: booking.oneWay.response.aggregateId,
        quoteRevision: '11111111-1111-4111-8111-111111111111',
        passengers: [
          {
            bookingPassengerId: '22222222-2222-4222-8222-222222222222',
            title: 'mr' as const,
            givenName: 'Demo',
            familyName: 'Traveler',
            dateOfBirth: '1990-04-12',
            gender: 'male',
            email: 'demo@example.test',
            phone: '+79161234567',
          },
        ],
      },
      booking.oneWay.response as FlightQuoteResponse,
      true,
      'owner-one',
      true,
    );
    http.expectOne('/api/flights/orders/hold').error(new ProgressEvent('error'));
    const { page, fixture, root } = createPage();
    await Promise.resolve();
    fillValid(page);
    page.submit();
    http.expectOne('/api/flights/search?currency=RUB').flush(fixtures.oneWay.response);
    page.checkOffer(fixtures.oneWay.response.offers[0].id);
    http.expectNone('/api/flights/orders/quote');
    expect(page.checkoutMessage()).toContain('требует проверки');
    fixture.detectChanges();
    expect(root.querySelector('[role="status"]')?.textContent).toContain('требует проверки');
  });

  it.each(['hold', 'confirm'] as const)(
    'does not allocate a quote during transient auth loss with unknown %s',
    async (kind) => {
      authStub.status.set({ kind: 'authenticated', userId: 'owner-one' });
      const operations = TestBed.inject(FlightOrderOperationsService);
      if (kind === 'hold') {
        operations.startHold(
          {
            aggregateId: booking.oneWay.response.aggregateId,
            quoteRevision: '11111111-1111-4111-8111-111111111111',
            passengers: [
              {
                bookingPassengerId: '22222222-2222-4222-8222-222222222222',
                title: 'mr' as const,
                givenName: 'Demo',
                familyName: 'Traveler',
                dateOfBirth: '1990-04-12',
                gender: 'male',
                email: 'demo@example.test',
                phone: '+79161234567',
              },
            ],
          },
          booking.oneWay.response as FlightQuoteResponse,
          true,
          'owner-one',
          true,
        );
      } else {
        operations.startConfirm(booking.oneWay.response.aggregateId, 'owner-one');
        await Promise.resolve();
      }
      http.expectOne(`/api/flights/orders/${kind}`).error(new ProgressEvent('error'));
      await Promise.resolve();
      await Promise.resolve();
      operations.denyAuthorization('Transient auth loss');
      const { page, fixture, root } = createPage();
      fillValid(page);
      page.submit();
      http.expectOne('/api/flights/search?currency=RUB').flush(fixtures.oneWay.response);
      page.checkOffer(fixtures.oneWay.response.offers[0].id);
      http.expectNone('/api/flights/orders/quote');
      fixture.detectChanges();
      expect(root.textContent).toContain('требует проверки');
    },
  );

  it('compares the previous route after a source-null login re-quote', async () => {
    authStub.status.set({ kind: 'authenticated', userId: 'owner-one' });
    const { page } = createPage();
    fillValid(page);
    page.submit();
    http.expectOne('/api/flights/search?currency=RUB').flush(fixtures.oneWay.response);
    page.checkOffer(fixtures.oneWay.response.offers[0].id);
    http.expectOne('/api/flights/orders/quote').flush(booking.oneWay.response);
    page.acceptQuote();
    await page.beginBooking();
    const previous = booking.oneWay.response.offer;
    http.expectOne('/api/flights/orders/quote').flush({
      ...booking.oneWay.response,
      priceChanged: false,
      offer: {
        ...previous,
        itinerary: {
          ...previous.itinerary,
          slices: [
            {
              ...previous.itinerary.slices[0],
              segments: [{ ...previous.itinerary.slices[0].segments[0], flightNumber: 'SU999' }],
            },
          ],
        },
      },
    });
    const state = page.quoteState();
    expect(state.kind).toBe('ready');
    if (state.kind === 'ready') {
      expect(state.routeChanged).toBe(true);
      expect(state.searchView).not.toBeNull();
      expect(state.accepted).toBe(false);
    }
  });

  it('ignores a quote response from the previous owner', () => {
    authStub.status.set({ kind: 'authenticated', userId: 'owner-one' });
    const { page } = createPage();
    fillValid(page);
    page.submit();
    http.expectOne('/api/flights/search?currency=RUB').flush(fixtures.oneWay.response);
    page.checkOffer(fixtures.oneWay.response.offers[0].id);
    const request = http.expectOne('/api/flights/orders/quote');
    authStub.status.set({ kind: 'authenticated', userId: 'owner-two' });
    request.flush(booking.oneWay.response);
    expect(page.quoteState().kind).toBe('idle');
    expect(page.checkoutStarted()).toBe(false);
  });

  it('does not resurrect a selection changed while login was pending', async () => {
    const { page } = createPage();
    fillValid(page);
    page.submit();
    http.expectOne('/api/flights/search?currency=RUB').flush(fixtures.oneWay.response);
    page.checkOffer(fixtures.oneWay.response.offers[0].id);
    http.expectOne('/api/flights/orders/quote').flush(booking.oneWay.response);
    page.acceptQuote();
    let finish!: (value: boolean) => void;
    authStub.beginLogin.mockImplementation(
      () =>
        new Promise<boolean>((resolve) => {
          finish = resolve;
        }),
    );
    const pending = page.beginBooking();
    page.form.controls.origin.setValue('LED');
    authStub.status.set({ kind: 'authenticated', userId: 'owner-one' });
    finish(true);
    await pending;
    http.expectNone('/api/flights/orders/quote');
    expect(page.quoteState().kind).toBe('idle');
  });

  it('requires quote acceptance inside beginBooking before login or checkout', async () => {
    const { page } = createPage();
    fillValid(page);
    page.submit();
    http.expectOne('/api/flights/search?currency=RUB').flush(fixtures.oneWay.response);
    page.checkOffer(fixtures.oneWay.response.offers[0].id);
    http.expectOne('/api/flights/orders/quote').flush(booking.oneWay.response);
    await page.beginBooking();
    expect(authStub.beginLogin).not.toHaveBeenCalled();
    expect(page.checkoutStarted()).toBe(false);
    http.expectNone('/api/flights/orders/quote');
  });

  it('rechecks the in-memory selection for an already authenticated owner', async () => {
    authStub.status.set({ kind: 'authenticated', userId: 'bfb631f9-c340-4f2a-bcd7-9427d17d8c59' });
    const { fixture, page, root } = createPage();
    fillValid(page);
    submit(root);
    http.expectOne('/api/flights/search?currency=RUB').flush(fixtures.oneWay.response);
    page.checkOffer(fixtures.oneWay.response.offers[0].id);
    http.expectOne('/api/flights/orders/quote').flush(booking.oneWay.response);
    page.acceptQuote();
    await page.beginBooking();
    const request = http.expectOne('/api/flights/orders/quote');
    expect(request.request.body).toEqual(booking.reQuoteChanged.request);
    request.flush(booking.reQuoteChanged.response);
    fixture.detectChanges();
    expect(root.querySelector('app-flight-booking-panel')).toBeNull();
    page.acceptQuote();
    page.acceptQuote();
    await page.beginBooking();
    fixture.detectChanges();
    expect(root.querySelector('app-flight-booking-panel')).not.toBeNull();
    expect(authStub.beginLogin).not.toHaveBeenCalled();
  });

  it('opens the demo passenger form after the user accepts the post-login re-quote', async () => {
    authStub.isDemo = true;
    authStub.beginLogin.mockImplementation(async () => {
      authStub.status.set({ kind: 'authenticated', userId: 'demo-only' });
      return true;
    });
    const { fixture, page, root } = createPage();
    fillValid(page);
    submit(root);
    http.expectOne('/api/flights/search?currency=RUB').flush(fixtures.oneWay.response);
    fixture.detectChanges();
    (root.querySelector('[data-action="quote"]') as HTMLButtonElement).click();
    http.expectOne('/api/flights/orders/quote').flush(booking.oneWay.response);
    fixture.detectChanges();
    (root.querySelector('[data-action="accept-quote"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    (root.querySelector('[data-action="start-booking"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    http.expectOne('/api/flights/orders/quote').flush(booking.reQuoteChanged.response);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(root.textContent).toContain('Проверьте предложение перед оформлением');
    expect(root.querySelector('app-flight-booking-panel')).toBeNull();

    (root.querySelector('[data-action="accept-quote"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    (root.querySelector('[data-action="start-booking"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(authStub.beginLogin).toHaveBeenCalledTimes(1);
    expect(root.querySelector('app-flight-booking-panel')).not.toBeNull();
  });
  it.each(['oneWay', 'groupTwo', 'groupNine'] as const)(
    'carries submitted %s count into quote intent and consumes the group total',
    (key) => {
      const { fixture, page, root } = createPage();
      fillValid(page);
      page.form.patchValue({ passengerCount: fixtures[key].request.passengerCount });
      page.submit();
      const search = http.expectOne('/api/flights/search?currency=RUB');
      expect(search.request.body.passengerCount).toBe(fixtures[key].request.passengerCount);
      search.flush(fixtures[key].response);
      fixture.detectChanges();
      page.checkOffer(fixtures[key].response.offers[0].id);
      const quote = http.expectOne('/api/flights/orders/quote');
      expect(quote.request.body.passengerCount).toBe(fixtures[key].request.passengerCount);
      quote.flush(booking[key].response);
      fixture.detectChanges();
      const current = page.quoteState();
      expect(current.kind).toBe('ready');
      if (current.kind !== 'ready') return;
      expect(current.intent.passengerCount).toBe(fixtures[key].request.passengerCount);
      expect(current.quote.offer.totalAmount).toBe(booking[key].response.offer.totalAmount);
      expect(current.accepted).toBe(false);
      if (key !== 'oneWay') expect(root.textContent).toContain('пропущены');
    },
  );
  it('clears acceptance on a new revision even when local member IDs, route and group price are retained', () => {
    const { fixture, page } = createPage();
    fillValid(page);
    page.form.patchValue({ passengerCount: 2 });
    page.submit();
    http.expectOne('/api/flights/search?currency=RUB').flush(fixtures.groupTwo.response);
    page.checkOffer(fixtures.groupTwo.response.offers[0].id);
    http.expectOne('/api/flights/orders/quote').flush(booking.groupTwo.response);
    page.acceptQuote();
    page.reQuote();
    http.expectOne('/api/flights/orders/quote').flush({
      ...booking.groupTwo.response,
      binding: { ...booking.groupTwo.response.binding, revision: '33333333-3333-4333-8333-333333333333' },
    });
    fixture.detectChanges();
    const state = page.quoteState();
    expect(state.kind).toBe('ready');
    if (state.kind === 'ready') {
      expect(state.accepted).toBe(false);
      expect(state.requiresAcceptance).toBe(true);
      expect(state.changed).toBe(true);
    }
  });
  it('retains nine same-owner local drafts through actual refresh while requiring a fresh review', async () => {
    authStub.status.set({ kind: 'authenticated', userId: 'refresh-owner' });
    const { fixture, page, root } = createPage();
    fillValid(page);
    page.form.patchValue({ passengerCount: 9 });
    page.submit();
    http.expectOne('/api/flights/search?currency=RUB').flush(fixtures.groupNine.response);
    page.checkOffer(fixtures.groupNine.response.offers[0].id);
    http.expectOne('/api/flights/orders/quote').flush(booking.groupNine.response);
    page.acceptQuote();
    await page.beginBooking();
    http.expectOne('/api/flights/orders/quote').flush({
      ...booking.groupNine.response,
      binding: { ...booking.groupNine.response.binding, revision: '33333333-3333-4333-8333-333333333333' },
    });
    page.acceptQuote();
    await page.beginBooking();
    fixture.detectChanges();
    const panel = fixture.debugElement.query(By.directive(FlightsBookingPanelComponent))
      .componentInstance as FlightsBookingPanelComponent;
    for (const [index, row] of panel.passengers.controls.entries())
      row.patchValue({
        title: 'dr',
        givenName: 'Demo',
        familyName: 'Traveler',
        dateOfBirth: '1990-04-12',
        gender: 'female',
        email: `demo${index}@example.test`,
        phone: '+79161234567',
      });
    page.refreshCheckoutQuote();
    fixture.detectChanges();
    expect(fixture.debugElement.query(By.directive(FlightsBookingPanelComponent))?.componentInstance).toBe(panel);
    expect(panel.passengers.at(8).controls.email.value).toBe('demo8@example.test');
    panel.hold();
    http.expectNone('/api/flights/orders/hold');
    const refresh = http.expectOne('/api/flights/orders/quote');
    expect(refresh.request.body.passengerCount).toBe(9);
    refresh.flush({
      ...booking.groupNine.response,
      binding: { ...booking.groupNine.response.binding, revision: '44444444-4444-4444-8444-444444444444' },
    });
    fixture.detectChanges();
    expect(panel.passengers.at(8).controls.email.value).toBe('demo8@example.test');
    expect(root.querySelector('[data-action="accept-quote"]')).not.toBeNull();
    panel.hold();
    http.expectNone('/api/flights/orders/hold');
    page.acceptQuote();
    fixture.detectChanges();
    panel.hold();
    await fixture.whenStable();
    const hold = http.expectOne('/api/flights/orders/hold');
    expect(JSON.parse(hold.request.body).passengers).toHaveLength(9);
    expect(JSON.parse(hold.request.body).quoteRevision).toBe('44444444-4444-4444-8444-444444444444');
    hold.error(new ProgressEvent('error'));
  });
  it('erases the actual mounted draft for changed local membership and owner identity', async () => {
    authStub.status.set({ kind: 'authenticated', userId: 'refresh-owner' });
    const { fixture, page } = createPage();
    fillValid(page);
    page.form.patchValue({ passengerCount: 9 });
    page.submit();
    http.expectOne('/api/flights/search?currency=RUB').flush(fixtures.groupNine.response);
    page.checkOffer(fixtures.groupNine.response.offers[0].id);
    http.expectOne('/api/flights/orders/quote').flush(booking.groupNine.response);
    page.acceptQuote();
    await page.beginBooking();
    http.expectOne('/api/flights/orders/quote').flush({
      ...booking.groupNine.response,
      binding: { ...booking.groupNine.response.binding, revision: '33333333-3333-4333-8333-333333333333' },
    });
    page.acceptQuote();
    await page.beginBooking();
    fixture.detectChanges();
    const panel = fixture.debugElement.query(By.directive(FlightsBookingPanelComponent))
      .componentInstance as FlightsBookingPanelComponent;
    for (const row of panel.passengers.controls) row.patchValue({ email: 'private@example.test' });
    page.refreshCheckoutQuote();
    const binding = {
      ...booking.groupNine.response.binding,
      revision: '44444444-4444-4444-8444-444444444444',
      slots: booking.groupNine.response.binding.slots.map((slot, index) =>
        index === 0 ? { ...slot, bookingPassengerId: '55555555-5555-4555-8555-555555555555' } : slot,
      ),
    };
    http.expectOne('/api/flights/orders/quote').flush({ ...booking.groupNine.response, binding });
    fixture.detectChanges();
    expect(fixture.debugElement.query(By.directive(FlightsBookingPanelComponent)).componentInstance).toBe(panel);
    expect(panel.passengers.controls.every((row) => row.controls.email.value === '')).toBe(true);
    panel.passengers.at(0).patchValue({ email: 'private-again@example.test' });
    authStub.status.set({ kind: 'authenticated', userId: 'another-owner' });
    fixture.detectChanges();
    expect(fixture.debugElement.query(By.directive(FlightsBookingPanelComponent))).toBeNull();
    expect(panel.passengers.controls.every((row) => row.controls.email.value === '')).toBe(true);
    http.expectNone('/api/flights/orders/hold');
  });
});
