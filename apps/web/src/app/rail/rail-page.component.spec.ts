import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { railDetailsFixture } from '../../../test-fixtures/rail-details-fixture';
import { railFixture } from '../../../test-fixtures/rail-fixture';
import { RailPageComponent } from './rail-page.component';

describe('Rail page', () => {
  it('announces detail-only expiry after removing all route facts while keeping current search', () => {
    TestBed.configureTestingModule({
      imports: [RailPageComponent],
      providers: [provideHttpClient(), provideRouter([])],
    });
    const fixture = TestBed.createComponent(RailPageComponent);
    const now = Date.now();
    const { result } = railFixture(now);
    const route = railDetailsFixture(now);
    const state = fixture.componentInstance.state;
    state.result = result;
    state.reference = {
      kind: 'complete',
      reasons: [],
      suggestions: [],
      hasMore: false,
      catalogueGeneration: result.request.catalogueGeneration,
      source: 'yandex-rasp',
      profile: 'fictional',
      profileEpoch: result.profileEpoch,
      receivedAt: result.receivedAt,
      expiresAt: result.expiresAt,
      attribution: result.attribution,
    };
    state.detail = { ...route.reply, expiresAt: new Date(now - 1).toISOString() };
    state.detailKey = result.items[0].key;
    state.resume();
    fixture.detectChanges();
    expect(state.detail).toBeNull();
    expect(state.result).toBe(result);
    expect(fixture.nativeElement.querySelector('#rail-route')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Срок подробностей истёк. Откройте маршрут заново.');
    expect(
      Array.from(fixture.nativeElement.querySelectorAll('[aria-live=polite]')).some((node) =>
        (node as HTMLElement).textContent?.includes('Срок подробностей истёк'),
      ),
    ).toBe(true);
  });
  it('distinguishes enforced current frame refusal from report-only and unrelated frames', () => {
    TestBed.configureTestingModule({
      imports: [RailPageComponent],
      providers: [provideHttpClient(), provideRouter([])],
    });
    const fixture = TestBed.createComponent(RailPageComponent);
    const { result } = railFixture(Date.now());
    fixture.componentInstance.state.result = result;
    fixture.componentInstance.state.reference = {
      kind: 'complete',
      reasons: [],
      suggestions: [],
      hasMore: false,
      catalogueGeneration: result.request.catalogueGeneration,
      source: 'yandex-rasp',
      profile: 'fictional',
      profileEpoch: result.profileEpoch,
      receivedAt: result.receivedAt,
      expiresAt: result.expiresAt,
      attribution: result.attribution,
    };
    const emit = (disposition: string, blockedURI: string) => {
      const event = new Event('securitypolicyviolation');
      Object.defineProperties(event, {
        effectiveDirective: { value: 'frame-src' },
        disposition: { value: disposition },
        blockedURI: { value: blockedURI },
      });
      document.dispatchEvent(event);
    };
    const source = new URL('/rail-fictional-banner.html', document.baseURI).href;
    emit('report', source);
    expect(fixture.componentInstance.state.result).not.toBeNull();
    emit('enforce', new URL('/', document.baseURI).href);
    expect(fixture.componentInstance.state.result).not.toBeNull();
    emit('enforce', 'https://identity.example/frame');
    expect(fixture.componentInstance.state.result).not.toBeNull();
    emit('enforce', source);
    expect(fixture.componentInstance.state.result).toBeNull();
    expect(fixture.componentInstance.state.reference).toBeNull();
  });
  it('renders all exact digits and creates a local sandboxed no-referrer frame', () => {
    TestBed.configureTestingModule({
      imports: [RailPageComponent],
      providers: [provideHttpClient(), provideRouter([])],
    });
    const fixture = TestBed.createComponent(RailPageComponent);
    const { result } = railFixture(Date.now());
    fixture.componentInstance.state.result = result;
    fixture.componentInstance.state.reference = {
      kind: 'complete',
      reasons: [],
      suggestions: [],
      hasMore: false,
      catalogueGeneration: result.request.catalogueGeneration,
      source: 'yandex-rasp',
      profile: 'fictional',
      profileEpoch: result.profileEpoch,
      receivedAt: result.receivedAt,
      expiresAt: result.expiresAt,
      attribution: result.attribution,
    };
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid=rail-price]').textContent).toContain(
      '9007199254740993.05 RUB',
    );
    const frame = fixture.nativeElement.querySelector('iframe');
    expect(frame.getAttribute('src')).toBe('/rail-fictional-banner.html');
    expect(frame.getAttribute('sandbox')).toBe('');
    expect(frame.getAttribute('referrerpolicy')).toBe('no-referrer');
  });
  it('renders independent ticket unknowns and exact decimal string through text', async () => {
    TestBed.configureTestingModule({
      imports: [RailPageComponent],
      providers: [provideHttpClient(), provideRouter([])],
    });
    const fixture = TestBed.createComponent(RailPageComponent);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Наличие мест неизвестно');
    expect(fixture.nativeElement.querySelector('input[type=date]')?.value).toBe('');
    expect(fixture.nativeElement.querySelector('form')).not.toBeNull();
  });
});
