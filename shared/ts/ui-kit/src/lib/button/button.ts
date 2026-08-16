import { Directive } from '@angular/core';
import { BrnButton } from '@spartan-ng/brain/button';

/**
 * Travel's public button primitive.
 *
 * Spartan stays behind this facade so product code depends on the Travel UI
 * contract rather than directly on a third-party component API.
 */
@Directive({
  selector: 'button[travelButton]',
  exportAs: 'travelButton',
  hostDirectives: [{ directive: BrnButton, inputs: ['disabled'] }],
  host: {
    type: 'button',
    'data-slot': 'button',
    class:
      "focus-visible:border-ring focus-visible:ring-ring/50 data-[matches-spartan-invalid=true]:ring-destructive/20 dark:data-[matches-spartan-invalid=true]:ring-destructive/40 data-[matches-spartan-invalid=true]:border-destructive dark:data-[matches-spartan-invalid=true]:border-destructive/50 rounded-lg border border-transparent bg-clip-padding text-sm font-medium focus-visible:ring-3 active:not-aria-[haspopup]:translate-y-px data-[matches-spartan-invalid=true]:ring-3 [&_ng-icon:not([class*='text-'])]:text-[length:--spacing(4)] group/button inline-flex h-8 shrink-0 items-center justify-center gap-1.5 whitespace-nowrap bg-primary px-2.5 text-primary-foreground outline-none transition-all select-none has-data-[icon=inline-end]:pe-2 has-data-[icon=inline-start]:ps-2 data-disabled:pointer-events-none data-disabled:opacity-50 [&_ng-icon]:pointer-events-none [&_ng-icon]:shrink-0",
  },
})
export class TravelButton {}
