import type { CanDeactivateFn } from '@angular/router';
export const tripsCanDeactivate: CanDeactivateFn<{ canLeave(): boolean }> = (component) => component.canLeave();
