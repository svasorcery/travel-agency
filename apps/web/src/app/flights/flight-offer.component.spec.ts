import { TestBed } from '@angular/core/testing';
import { FlightOfferComponent } from './flight-offer.component';
import type { BookableOfferView } from './flight-results';

describe('FlightOfferComponent', () => {
  it('shows each segment of a connecting route with its own departure and arrival offset', async () => {
    await TestBed.configureTestingModule({ imports: [FlightOfferComponent] }).compileComponents();
    const fixture = TestBed.createComponent(FlightOfferComponent);
    const offer: BookableOfferView = {
      kind: 'bookable',
      passengerCount: 1,
      holdEligible: true,
      holdIneligibilityReason: null,
      id: 'fixture-connecting',
      provider: 'duffel',
      price: '18 000 RUB',
      totalDuration: '14 ч 0 мин',
      slices: [
        {
          origin: 'LED',
          destination: 'DME',
          departure: '10.06.2030, 22:30 UTC+03:00',
          arrival: '11.06.2030, 12:30 UTC+03:00',
          duration: '14 ч 0 мин',
          transferCount: 1,
          segments: [
            {
              route: 'LED → DXB',
              departure: '10.06.2030, 22:30 UTC+03:00',
              arrival: '11.06.2030, 05:30 UTC+04:00',
              flight: 'SU SU310',
            },
            {
              route: 'DXB → DME',
              departure: '11.06.2030, 07:30 UTC+04:00',
              arrival: '11.06.2030, 12:30 UTC+03:00',
              flight: 'EK EK311',
            },
          ],
        },
      ],
    };
    fixture.componentRef.setInput('offer', offer);
    fixture.detectChanges();

    const content = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(content).toContain('LED → DXB');
    expect(content).toContain('DXB → DME');
    expect(content).toContain('05:30 UTC+04:00');
    expect(content).toContain('12:30 UTC+03:00');
    expect(content).toContain('Источник: duffel');
    expect(
      (fixture.nativeElement as HTMLElement).querySelectorAll('ol[aria-label="Сегменты перелёта"] li'),
    ).toHaveLength(2);
  });
});
