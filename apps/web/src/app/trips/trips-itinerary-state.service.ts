import { HttpErrorResponse } from '@angular/common/http';
import { computed, effect, Injectable, InjectionToken, inject, signal, untracked } from '@angular/core';
import {
  type TripItineraryAvailability,
  type TripItineraryCandidate,
  type TripItineraryComputed,
  type TripPlan,
  TripsItineraryApiService,
  TripsItineraryRequestError,
} from '@travel/api-client';
import { firstValueFrom, type Observable, Subject, takeUntil } from 'rxjs';
import { type TripsEmptyFoundation, TripsStateService } from './trips-state.service';

export const TRIPS_ITINERARY_CLOCK = new InjectionToken<() => number>('Trips itinerary clock', {
  providedIn: 'root',
  factory: () => Date.now,
});
export type TripsItineraryAvailabilityState =
  | { kind: 'unknown' | 'checking' | 'disabled' | 'unauthenticated' | 'forbidden' | 'unavailable' }
  | { kind: 'ready'; value: TripItineraryAvailability };
export type TripsItineraryStatus =
  | 'idle'
  | 'generating'
  | 'candidate'
  | 'partial'
  | 'empty'
  | 'unsupported'
  | 'checking-adoption'
  | 'staged'
  | 'stale'
  | 'expired'
  | 'busy'
  | 'deadline'
  | 'conflict'
  | 'uncertain'
  | 'disabled'
  | 'unauthenticated'
  | 'forbidden'
  | 'unavailable';
type Operation = { scope: object; foundation: TripsEmptyFoundation | null; cancel: Subject<void> };
type Preview = { foundation: TripsEmptyFoundation; value: TripItineraryCandidate };

/** Ephemeral presentation/application state. M1 remains the sole editor and CAS authority. */
@Injectable({ providedIn: 'root' })
export class TripsItineraryStateService {
  private readonly editor = inject(TripsStateService);
  private readonly api = inject(TripsItineraryApiService);
  private readonly now = inject(TRIPS_ITINERARY_CLOCK);
  private readonly scope = signal<object | null>(null);
  private readonly available = signal<TripsItineraryAvailabilityState>({ kind: 'unknown' });
  private readonly value = signal<Preview | null>(null);
  private readonly phase = signal<TripsItineraryStatus>('idle');
  private readonly operation = signal<Operation | null>(null);
  private readonly live = computed(() => this.scope() !== null && this.scope() === this.editor.itineraryContext());
  readonly availability = computed<TripsItineraryAvailabilityState>(() =>
    this.live() ? this.available() : { kind: 'unknown' },
  );

  constructor() {
    effect(() => {
      const context = this.editor.itineraryContext(),
        scope = this.scope();
      const preview = this.value(),
        op = this.operation();
      const valid =
        (!preview || this.editor.isEmptyFoundationCurrent(preview.foundation)) &&
        (!op?.foundation || this.editor.isEmptyFoundationCurrent(op.foundation));
      untracked(() => {
        if (scope !== null && context !== scope) this.leave();
        else if (!valid) this.clear('stale');
      });
    });
  }
  // Functions deliberately read the injected clock on every access; no cached clock computation or polling.
  preview(): TripItineraryCandidate | null {
    const p = this.value();
    return this.live() && p && this.editor.isEmptyFoundationCurrent(p.foundation) ? p.value : null;
  }
  status(): TripsItineraryStatus {
    if (!this.live()) return 'idle';
    if (this.editor.status() === 'uncertain') return 'uncertain';
    if (this.editor.mutationBlocked()) return 'conflict';
    const p = this.value(),
      op = this.operation();
    if (
      (p && !this.editor.isEmptyFoundationCurrent(p.foundation)) ||
      (op?.foundation && !this.editor.isEmptyFoundationCurrent(op.foundation))
    ) {
      return this.editor.status() === 'uncertain' ? 'uncertain' : this.editor.mutationBlocked() ? 'conflict' : 'stale';
    }
    if (p && !this.fresh(p.value)) return 'expired';
    return this.phase();
  }
  pending(): 'generating' | 'checking-adoption' | null {
    const status = this.status();
    return status === 'generating' || status === 'checking-adoption' ? status : null;
  }
  canGenerate(): boolean {
    const availability = this.availability();
    return (
      this.live() &&
      this.operation() === null &&
      availability.kind === 'ready' &&
      this.future(availability.value.releaseValidUntil) &&
      this.editor.captureEmptyFoundation() !== null
    );
  }
  canAdopt(): boolean {
    const p = this.value(),
      available = this.availability();
    return (
      this.live() &&
      this.operation() === null &&
      !!p &&
      this.editor.isEmptyFoundationCurrent(p.foundation) &&
      p.value.outcome === 'candidate' &&
      this.fresh(p.value) &&
      available.kind === 'ready' &&
      this.samePackage(p.value, available.value)
    );
  }
  // Presentation-only continuation: keep the preview's own foundation across its validation read.
  // Never expose this guard as saved/Rail/staging authority; the existing adopt path owns all writes.
  captureAdoptionErrorFocusGuard(): (() => boolean) | null {
    const preview = this.value(),
      scope = this.scope();
    if (!preview || !this.live() || !this.editor.isEmptyFoundationCurrent(preview.foundation)) return null;
    const foundation = preview.foundation;
    return () =>
      this.scope() === scope &&
      this.live() &&
      this.editor.isEmptyFoundationCurrent(foundation) &&
      this.operation() === null &&
      this.value() === null &&
      this.phase() !== 'idle';
  }
  async activate(): Promise<void> {
    if (this.live() && this.available().kind !== 'unknown') return;
    await this.refreshAvailability();
  }
  async refreshAvailability(): Promise<void> {
    this.leave();
    const scope = this.editor.itineraryContext();
    if (scope === null) return;
    this.scope.set(scope);
    this.available.set({ kind: 'checking' });
    const op = this.start(null);
    try {
      const result = await this.request(op, (token) => this.api.availability(token));
      if (!this.current(op)) return;
      if (!this.future(result.releaseValidUntil)) throw new Error('Expired release.');
      this.available.set({ kind: 'ready', value: result });
    } catch (error) {
      if (this.current(op)) this.availabilityFailure(error);
    } finally {
      this.finish(op);
    }
  }
  async generate(): Promise<void> {
    if (!this.canGenerate()) return;
    const foundation = this.editor.captureEmptyFoundation(),
      available = this.availability();
    if (!foundation || available.kind !== 'ready') return;
    this.clear('generating');
    const op = this.start(foundation);
    try {
      const result = await this.request(op, (token) =>
        this.api.candidate(foundation.plan.tripId, foundation.plan.revision, token),
      );
      if (!this.current(op)) return this.stale(op);
      if (!this.matchesFoundation(result, foundation.plan)) throw new Error('Candidate context mismatch.');
      if (result.outcome !== 'unsupported' && !this.samePackage(result, available.value)) {
        this.clear('stale');
        return;
      }
      if (!this.fresh(result)) {
        this.clear('expired');
        return;
      }
      this.value.set({ foundation, value: result });
      this.phase.set(result.outcome === 'candidate' && result.coverage === 'partial' ? 'partial' : result.outcome);
    } catch (error) {
      if (this.current(op)) this.clear(this.errorStatus(error));
      else this.stale(op);
    } finally {
      this.finish(op);
    }
  }
  async adopt(): Promise<boolean> {
    if (!this.canAdopt()) {
      this.checkExpiry();
      return false;
    }
    const preview = this.value();
    if (!preview) return false;
    this.phase.set('checking-adoption');
    const op = this.start(preview.foundation);
    try {
      // Neither read advances the editor baseline; both belong to this exact operation and preview.
      const [owner, available] = await Promise.all([
        this.editor.readEmptyFoundation(preview.foundation, op.cancel),
        this.request(op, (token) => this.api.availability(token)),
      ]);
      if (!this.current(op) || this.value() !== preview) {
        this.stale(op);
        return false;
      }
      if (!owner || !this.sameOwnerFoundation(owner, preview.foundation.plan)) {
        this.clear('stale');
        return false;
      }
      if (preview.value.outcome !== 'candidate') {
        this.clear('stale');
        return false;
      }
      this.available.set({ kind: 'ready', value: available });
      if (!this.samePackage(preview.value, available)) {
        this.clear('stale');
        return false;
      }
      if (!this.fresh(preview.value) || !this.future(available.releaseValidUntil)) {
        this.clear('expired');
        return false;
      }
      if (!this.matchesFoundation(preview.value, preview.foundation.plan)) {
        this.clear('unavailable');
        return false;
      }
      const days = preview.value.days.map((day) => ({
        date: day.date,
        blocks: day.blocks.map((block) => ({
          blockId: block.proposedBlockId,
          title: block.title,
          note: block.note,
          startTime: null,
          endTime: null,
          estimate: null,
        })),
      }));
      // Final synchronous combined guard, including a new clock read immediately before ordinary M1 edit.
      if (
        !this.current(op) ||
        this.value() !== preview ||
        !this.fresh(preview.value) ||
        !this.future(available.releaseValidUntil)
      ) {
        this.clear('expired');
        return false;
      }
      const staged = this.editor.stageEmptyFoundation(preview.foundation, days);
      this.clear(staged ? 'staged' : 'stale');
      return staged;
    } catch (error) {
      if (this.current(op) && this.value() === preview) {
        this.availabilityFailure(error);
        this.clear(this.errorStatus(error));
      } else this.stale(op);
      return false;
    } finally {
      this.finish(op);
    }
  }
  dismiss(): void {
    this.clear('idle');
  }
  leave(): void {
    this.clear('idle');
    this.scope.set(null);
    this.available.set({ kind: 'unknown' });
  }
  checkExpiry(): void {
    const p = this.value();
    if (p && !this.fresh(p.value)) this.clear('expired');
  }
  private start(foundation: TripsEmptyFoundation | null): Operation {
    const scope = this.scope();
    if (scope === null) throw new Error('Missing itinerary context.');
    const op = { scope, foundation, cancel: new Subject<void>() };
    this.operation.set(op);
    return op;
  }
  private current(op: Operation): boolean {
    return (
      this.operation() === op &&
      this.live() &&
      op.scope === this.scope() &&
      (!op.foundation || this.editor.isEmptyFoundationCurrent(op.foundation))
    );
  }
  private stale(op: Operation): void {
    if (this.operation() === op) this.clear(this.editor.status() === 'uncertain' ? 'uncertain' : 'stale');
  }
  private finish(op: Operation): void {
    if (this.operation() === op) this.operation.set(null);
    op.cancel.next();
    op.cancel.complete();
  }
  private clear(status: TripsItineraryStatus): void {
    const op = this.operation();
    this.operation.set(null);
    op?.cancel.next();
    op?.cancel.complete();
    this.value.set(null);
    this.phase.set(status);
  }
  private async request<T>(op: Operation, call: (token: string | null) => Observable<T>): Promise<T> {
    const token = await this.editor.auth.accessToken();
    if (!this.current(op)) throw new Error('Context changed.');
    return firstValueFrom(call(token).pipe(takeUntil(op.cancel)));
  }
  private availabilityFailure(error: unknown): void {
    const status = this.errorStatus(error);
    this.available.set({
      kind: status === 'disabled' || status === 'unauthenticated' || status === 'forbidden' ? status : 'unavailable',
    });
    this.phase.set(status);
  }
  private errorStatus(error: unknown): TripsItineraryStatus {
    if (error instanceof TripsItineraryRequestError) {
      switch (error.kind) {
        case 'disabled':
        case 'unauthenticated':
        case 'forbidden':
        case 'busy':
        case 'deadline':
        case 'conflict':
        case 'stale':
          return error.kind;
        case 'not-found':
          return 'stale';
        default:
          return 'unavailable';
      }
    }
    if (error instanceof HttpErrorResponse) {
      if (error.status === 401) return 'unauthenticated';
      if (error.status === 403) return 'forbidden';
      if (error.status === 404 || error.status === 412) return 'stale';
    }
    return 'unavailable';
  }
  private future(utc: string): boolean {
    return Date.parse(utc) > this.now();
  }
  private fresh(candidate: TripItineraryCandidate): boolean {
    return (
      candidate.outcome === 'unsupported' ||
      (this.future(candidate.expiresAt) && this.future(candidate.releaseValidUntil))
    );
  }
  private samePackage(candidate: TripItineraryComputed, ready: TripItineraryAvailability): boolean {
    return (
      candidate.candidateVersion === ready.candidateVersion &&
      candidate.destinationId === ready.destinationId &&
      candidate.datasetId === ready.datasetId &&
      candidate.datasetVersion === ready.datasetVersion &&
      candidate.datasetHash === ready.datasetHash &&
      candidate.policyVersion === ready.policyVersion &&
      candidate.dataMode === ready.dataMode &&
      candidate.humanQuality === ready.humanQuality &&
      candidate.releaseValidUntil === ready.releaseValidUntil
    );
  }
  private sameOwnerFoundation(owner: TripPlan, captured: TripPlan): boolean {
    return (
      owner.tripId === captured.tripId &&
      owner.revision === captured.revision &&
      owner.title === captured.title &&
      JSON.stringify(owner.brief) === JSON.stringify(captured.brief) &&
      JSON.stringify(owner.destination) === JSON.stringify(captured.destination) &&
      JSON.stringify(owner.days) === JSON.stringify(captured.days)
    );
  }
  private matchesFoundation(candidate: TripItineraryCandidate, plan: TripPlan): boolean {
    if (
      candidate.baseRevision !== plan.revision ||
      candidate.destinationId !== plan.destination.destinationId ||
      candidate.timeZone !== plan.destination.timeZone ||
      candidate.catalogueVersion !== plan.destination.catalogueVersion
    )
      return false;
    if (candidate.outcome === 'unsupported') return true;
    const brief = plan.brief,
      requested = new Set<string>(brief.interests);
    const [target, walkCap, totalCap] =
      brief.pace === 'relaxed' ? [2, 60, 240] : brief.pace === 'balanced' ? [3, 90, 360] : [4, 120, 480];
    if (
      candidate.days.length !== plan.days.length ||
      candidate.days.some(
        (day, i) =>
          day.date !== plan.days[i].date ||
          day.blocks.length > target ||
          day.blocks.some((block) => block.date !== day.date) ||
          day.edges.reduce((sum, edge) => sum + edge.maximumMinutes, 0) > walkCap ||
          day.edges.reduce((sum, edge) => sum + edge.maximumMinutes, 0) +
            day.blocks.reduce((sum, block) => sum + block.dwellMinutes, 0) >
            totalCap,
      )
    )
      return false;
    const covered = new Set(
      candidate.days.flatMap((day) => day.blocks.flatMap((block) => block.tags)).filter((tag) => requested.has(tag)),
    );
    const unmet = new Set([...requested].filter((tag) => !covered.has(tag)));
    const equals = (actual: string[], expected: Set<string>) =>
      actual.length === expected.size && actual.every((value) => expected.has(value));
    if (!equals(candidate.coveredInterests, covered) || !equals(candidate.unmetInterests, unmet)) return false;
    const targetsFilled = candidate.days.every((day) => day.blocks.length === target);
    const filled = targetsFilled && unmet.size === 0 && !candidate.searchLimited;
    return (
      candidate.coverage === (filled ? 'filled' : 'partial') &&
      candidate.reasons.includes('insufficientEligibleStops') === !targetsFilled &&
      candidate.exclusions.includes('transferNotPlanned') === (brief.originId !== null) &&
      candidate.exclusions.includes('totalCostUnknown') === (brief.totalBudget !== null)
    );
  }
}
