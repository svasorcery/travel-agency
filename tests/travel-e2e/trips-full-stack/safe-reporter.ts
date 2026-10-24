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
  'complete-service-restart',
  'fresh-login-and-compare',
  'foreign-owner-refusals',
  'stale-revision-refusal',
  'private-evidence',
] as const;
export type Stage = (typeof stages)[number];

function safeStatus(status: string): string {
  return ['passed', 'failed', 'timedOut', 'skipped', 'interrupted'].includes(status) ? status : 'failed';
}

/** No error, title, URL, attachment, stdout or stderr is trusted for publication. */
export default class SafeTripsReporter implements Reporter {
  printsToStdio(): boolean {
    return true;
  }
  onStdOut(): void {}
  onStdErr(): void {}
  onError(): void {
    process.stdout.write('TRIPS_E2E runner failed; private error details suppressed.\n');
  }
  onTestEnd(test: TestCase, result: TestResult): void {
    const candidate = test.annotations
      .filter((annotation) => annotation.type === 'trips-safe-stage')
      .at(-1)?.description;
    const stage = stages.find((value) => value === candidate) ?? 'validate-owned-state';
    process.stdout.write(`TRIPS_E2E ${safeStatus(result.status)} stage=${stage}\n`);
  }
  onEnd(result: FullResult): void {
    process.stdout.write(`TRIPS_E2E complete status=${safeStatus(result.status)}\n`);
  }
}
