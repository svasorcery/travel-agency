import type { FullResult, Reporter, TestCase, TestResult } from '@playwright/test/reporter';

export const stages = [
  'validate-owned-state',
  'anonymous-refusal',
  'owner-login',
  'create-foundation',
  'generate-without-write',
  'adopt-without-write',
  'edit-and-save',
  'list-and-reopen',
  'historical-moscow-save',
  'complete-service-restart',
  'fresh-login-and-compare',
  'foreign-owner-refusals',
  'stale-revision-refusal',
  'private-evidence',
  'seed-flight-orders',
  'read-seeded-event-count',
  'choose-flight-order',
  'attach-flight-order',
  'observe-linked-order',
  'inspect-flight-link-storage',
  'delete-linked-trip',
  'verify-link-cascade',
  'close-flight-order',
  'refresh-closed-order',
  'unlink-flight-order',
] as const;
export type Stage = (typeof stages)[number];
const failureCategories = ['assertion', 'timeout', 'syntax', 'operation', 'unknown'] as const;
export type SafeFailure = {
  errorClass: 'Error' | 'TypeError' | 'SyntaxError' | 'RangeError' | 'TimeoutError' | 'OtherError' | 'NonError';
  failureCategory: (typeof failureCategories)[number];
  responseFailure?:
    | 'body-resource-missing'
    | 'body-cache-evicted'
    | 'body-unavailable'
    | 'body-protocol-error'
    | 'browser-closed';
};

/** Preserve only known classifications, never exception names, messages, stacks or matcher values. */
export function safeFailure(error: unknown): SafeFailure {
  if (!(error instanceof Error)) return { errorClass: 'NonError', failureCategory: 'unknown' };
  const knownClasses = ['Error', 'TypeError', 'SyntaxError', 'RangeError', 'TimeoutError'] as const;
  const errorClass = knownClasses.find((known) => known === error.name) ?? 'OtherError';
  const failureCategory =
    'matcherResult' in error
      ? 'assertion'
      : errorClass === 'TimeoutError'
        ? 'timeout'
        : errorClass === 'SyntaxError'
          ? 'syntax'
          : errorClass === 'OtherError'
            ? 'unknown'
            : 'operation';
  const knownBodyFailures = [
    ['No resource with given identifier found', 'body-resource-missing'],
    ['No data found for resource with given identifier', 'body-resource-missing'],
    ['Request content was evicted from inspector cache', 'body-cache-evicted'],
    ['Response body is unavailable', 'body-unavailable'],
    ['Protocol error (Network.getResponseBody)', 'body-protocol-error'],
    ['Target page, context or browser has been closed', 'browser-closed'],
  ] as const;
  const responseFailure = knownBodyFailures.find(([fragment]) => error.message.includes(fragment))?.[1];
  return { errorClass, failureCategory, ...(responseFailure ? { responseFailure } : {}) };
}

function safeStatus(status: string): string {
  return ['passed', 'failed', 'timedOut', 'skipped', 'interrupted'].includes(status) ? status : 'failed';
}

/** No error, title, URL, attachment, stdout or stderr is trusted for publication. */
export default class SafeTripsReporter implements Reporter {
  printsToStdio(): boolean {
    return true;
  }
  onStdOut(): void {
    // Intentionally discard worker output that may contain private login or request data.
  }
  onStdErr(): void {
    // Intentionally discard raw errors; only allowlisted stage/status messages are public.
  }
  onError(): void {
    process.stdout.write('TRIPS_E2E runner failed; private error details suppressed.\n');
  }
  onTestEnd(test: TestCase, result: TestResult): void {
    const annotations = result.annotations ?? test.annotations;
    const candidate = annotations.filter((annotation) => annotation.type === 'trips-safe-stage').at(-1)?.description;
    const stage = stages.find((value) => value === candidate) ?? 'validate-owned-state';
    const classification = annotations.filter((a) => a.type === 'trips-safe-failure-category').at(-1)?.description;
    const category =
      failureCategories.find((value) => value === classification) ??
      (result.status === 'timedOut' ? 'timeout' : undefined);
    const status = safeStatus(result.status);
    const diagnostic =
      category && ['failed', 'timedOut', 'interrupted'].includes(status) ? ' category=' + category : '';
    process.stdout.write(`TRIPS_E2E ${status} stage=${stage}${diagnostic}\n`);
  }
  onEnd(result: FullResult): void {
    process.stdout.write(`TRIPS_E2E complete status=${safeStatus(result.status)}\n`);
  }
}
