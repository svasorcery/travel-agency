import { TestBed } from '@angular/core/testing';
import type { FlightRanking, FlightSearchResponse } from '@travel/api-client';
// eslint-disable-next-line @nx/enforce-module-boundaries
import fixtures from '../../../../../tests/fixtures/flights-search.json';
import { FlightOfferComponent } from './flight-offer.component';
import { summarizeSearchResponse } from './flight-results';

function rankedResponse(): FlightSearchResponse & { ranking: FlightRanking } {
  const offers = structuredClone(fixtures.oneWay.response.offers) as FlightSearchResponse['offers'];
  offers[1].totalAmount = offers[0].totalAmount;
  return {
    offers,
    partialFailures: [],
    ranking: {
      policy: 'price-first-v1',
      requestedCurrency: 'RUB',
      entries: offers.map((offer, index) => ({
        offerId: offer.id,
        currency: 'RUB',
        rank: index + 1,
        sourceAmount: offer.totalAmount,
        sourceCurrency: 'RUB',
        priceState: 'native',
        durationSeconds: index === 0 ? 7200 : null,
        transfers: index === 0 ? 0 : null,
        limitations: index === 0 ? [] : ['partial-itinerary'],
      })),
    },
  };
}

describe('ranking presentation', () => {
  it('keeps backend order and attaches facts only to their offers', () => {
    const response = rankedResponse();
    const view = summarizeSearchResponse(response);
    expect(view.rankingAvailable).toBe(true);
    expect(view.offers.map((o) => o.id)).toEqual(response.offers.map((o) => o.id));
    expect(view.offers[0].ranking?.duration).toBe('2 ч 0 мин');
    expect(view.offers[1].ranking?.duration).toBe('Длительность неизвестна');
    expect(view.offers[1].ranking?.transfers).toBe('Пересадки неизвестны');
  });
  it('leaves legacy responses without invented explanations', () => {
    expect(summarizeSearchResponse(fixtures.oneWay.response as FlightSearchResponse).rankingAvailable).toBe(false);
  });
  it('explains a failed conversion in the actual currency group', () => {
    const response = rankedResponse();
    response.offers = [response.offers[0]];
    response.offers[0].currency = 'EUR';
    response.offers[0].totalAmount = 100;
    response.ranking.entries = [
      {
        ...response.ranking.entries[0],
        currency: 'EUR',
        sourceCurrency: 'EUR',
        sourceAmount: 100,
        priceState: 'fx-unavailable',
        limitations: ['fx-unavailable'],
      },
    ];
    expect(summarizeSearchResponse(response).offers[0].ranking?.priceNote).toContain('сравнение только внутри EUR');
  });
  it('renders an accessible disclosure and honest unknown partner factors', async () => {
    await TestBed.configureTestingModule({ imports: [FlightOfferComponent] }).compileComponents();
    const fixture = TestBed.createComponent(FlightOfferComponent);
    fixture.componentRef.setInput('offer', summarizeSearchResponse(rankedResponse()).offers[1]);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('details summary')?.textContent).toContain('Почему здесь');
    expect(element.textContent).toContain('Длительность неизвестна');
    expect(element.textContent).toContain('Пересадки неизвестны');
    expect(element.textContent).not.toContain('Без пересадок');
  });
});
