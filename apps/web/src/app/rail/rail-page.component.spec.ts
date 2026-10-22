import { provideHttpClient } from '@angular/common/http';
import { ChangeDetectorRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { railFixture } from '../../../test-fixtures/rail-fixture';
import { RailPageComponent } from './rail-page.component';
import { RailSearchComponent } from './rail-search.component';

describe('Rail page', () => {
  it('hosts one reusable semantic search without Trips actions or duplicate page landmarks', () => {
    TestBed.configureTestingModule({
      imports: [RailPageComponent],
      providers: [provideHttpClient(), provideRouter([])],
    });
    const fixture = TestBed.createComponent(RailPageComponent);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('app-rail-search')).toHaveLength(1);
    expect(fixture.nativeElement.querySelectorAll('main')).toHaveLength(1);
    expect(fixture.nativeElement.querySelectorAll('h1')).toHaveLength(1);
    expect(fixture.nativeElement.textContent).not.toContain('Добавить намерение');
  });
  it('distinguishes enforced current frame refusal from report-only and unrelated frames', () => {
    TestBed.configureTestingModule({
      imports: [RailPageComponent],
      providers: [provideHttpClient(), provideRouter([])],
    });
    const fixture = TestBed.createComponent(RailPageComponent);
    fixture.detectChanges();
    const search = fixture.debugElement.query(By.directive(RailSearchComponent))
      .componentInstance as RailSearchComponent;
    const { result } = railFixture(Date.now());
    search.state.result = result;
    search.state.reference = {
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
    expect(search.state.result).not.toBeNull();
    emit('enforce', new URL('/', document.baseURI).href);
    expect(search.state.result).not.toBeNull();
    emit('enforce', 'https://identity.example/frame');
    expect(search.state.result).not.toBeNull();
    emit('enforce', source);
    expect(search.state.result).toBeNull();
    expect(search.state.reference).toBeNull();
  });
  it('renders all exact digits and creates a local sandboxed no-referrer frame', () => {
    TestBed.configureTestingModule({
      imports: [RailPageComponent],
      providers: [provideHttpClient(), provideRouter([])],
    });
    const fixture = TestBed.createComponent(RailPageComponent);
    fixture.detectChanges();
    const search = fixture.debugElement.query(By.directive(RailSearchComponent))
      .componentInstance as RailSearchComponent;
    const { result } = railFixture(Date.now());
    search.state.result = result;
    search.state.reference = {
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
    fixture.debugElement.query(By.directive(RailSearchComponent)).injector.get(ChangeDetectorRef).markForCheck();
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
