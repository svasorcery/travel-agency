import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  type ElementRef,
  Injector,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import type { TripHotelLink } from '@travel/api-client';
import { TripsHotelLinkStateService } from './trips-hotel-link-state.service';

@Component({
  selector: 'app-trips-hotel-link',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './trips-hotel-link.component.html',
  styles: [
    `
    section { margin-block:1.5rem; border-block-start:1px solid oklch(0.83 0.015 165); padding-block-start:1.25rem }
    h2 { font-size:1.4rem; font-weight:650 }
    p { margin-block:.5rem; overflow-wrap:anywhere }
    button, a { display:inline-block; min-height:44px; padding:.55rem .8rem; margin:.15rem .4rem .15rem 0 }
    button { font:inherit; border:1px solid currentColor; border-radius:6px }
  `,
  ],
})
export class TripsHotelLinkComponent {
  readonly state = inject(TripsHotelLinkStateService);
  readonly inCalendar = input(false);
  readonly confirmation = signal<TripHotelLink | null>(null);
  private readonly injector = inject(Injector);
  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');
  private readonly unlinkButton = viewChild<ElementRef<HTMLButtonElement>>('unlinkButton');
  private readonly confirmButton = viewChild<ElementRef<HTMLButtonElement>>('confirmButton');

  ask() {
    if (!this.state.canUnlink()) return;
    this.confirmation.set(this.state.link());
    afterNextRender(() => this.confirmButton()?.nativeElement.focus(), { injector: this.injector });
  }

  cancel() {
    this.confirmation.set(null);
    afterNextRender(() => this.unlinkButton()?.nativeElement.focus(), { injector: this.injector });
  }

  async unlink() {
    if (this.confirmation() !== this.state.link() || !this.state.canUnlink()) return;
    this.confirmation.set(null);
    afterNextRender(() => this.heading()?.nativeElement.focus(), { injector: this.injector });
    await this.state.unlink();
  }
}
