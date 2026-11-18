import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  type ElementRef,
  Injector,
  inject,
  input,
  viewChild,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import type { FlightLinkCandidate, FlightLinkOperation, LinkedBookingStatus } from '@travel/api-client';
import { TripsFlightLinksStateService } from './trips-flight-links-state.service';

@Component({
  selector: 'app-trip-flight-summary',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p>{{labels[order().bookingStatus]}} · Пассажиров: {{order().passengerCount}}</p>
    @if(order().bookingStatus==='Refunded'){<p>Зачисление денег уточняйте на странице заказа.</p>}
    @if(order().route; as legs){
      <ol>@for(leg of legs;track $index){
        <li><strong>{{leg.origin}} → {{leg.destination}}</strong><br>
          <time [attr.datetime]="leg.departAt">{{leg.departAt}}</time> → <time [attr.datetime]="leg.arriveAt">{{leg.arriveAt}}</time></li>
      }</ol>
    }@else{<p>Маршрут заказа недоступен.</p>}
  `,
})
export class TripFlightSummaryComponent {
  readonly order = input.required<FlightLinkCandidate>();
  readonly labels: Record<LinkedBookingStatus, string> = {
    Held: 'Бронь удерживается',
    Confirmed: 'Подтверждён',
    Ticketed: 'Выписан',
    Cancelled: 'Отменён',
    Refunded: 'Возврат по данным Flights',
  };
}
@Component({
  selector: 'app-trips-flight-links',
  imports: [RouterLink, TripFlightSummaryComponent],
  providers: [TripsFlightLinksStateService],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './trips-flight-links.component.html',
  styles: [
    `
    section { margin-block:1.5rem; border-block-start:1px solid oklch(0.83 0.015 165); padding-block-start:1.25rem }
    article { margin-block:1rem; border-inline-start:3px solid oklch(0.72 0.04 165); padding-inline-start:1rem }
    button, a { display:inline-block; min-height:44px; padding:.55rem .8rem; margin:.15rem .4rem .15rem 0 }
    button { font:inherit; border:1px solid currentColor; border-radius:6px }
    p { margin-block:.5rem } h2,h3 { font-weight:650 } h2 {font-size:1.4rem}
  `,
  ],
})
export class TripsFlightLinksComponent {
  readonly state = inject(TripsFlightLinksStateService);
  readonly draftDirty = input(false);
  private readonly injector = inject(Injector);
  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');
  private readonly chooser = viewChild<ElementRef<HTMLButtonElement>>('chooser');
  private readonly pickerHeading = viewChild<ElementRef<HTMLElement>>('pickerHeading');
  linked(id: string) {
    return this.state.saved()?.links.some((link) => link.orderId === id) === true;
  }
  async open() {
    if (this.draftDirty()) return;
    const action = this.state.loadCandidates();
    afterNextRender(() => this.pickerHeading()?.nativeElement.focus(), { injector: this.injector });
    await action;
  }
  close() {
    this.state.closePicker();
    afterNextRender(() => this.chooser()?.nativeElement.focus(), { injector: this.injector });
  }
  async mutate(op: FlightLinkOperation, id: string) {
    if (this.draftDirty()) return;
    const heading = this.heading()?.nativeElement;
    await this.state.mutate(op, id);
    if (heading?.isConnected && heading.ownerDocument.activeElement === heading.ownerDocument.body) heading.focus();
  }
}
