import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
// Shared canonical HTTP fixture, test only.
// eslint-disable-next-line @nx/enforce-module-boundaries
import fixtures from '../../../../../tests/fixtures/flights-search.json';
import { FlightsPageComponent } from './flights-page.component';

describe('FlightsPageComponent', () => {
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [FlightsPageComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
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
});
