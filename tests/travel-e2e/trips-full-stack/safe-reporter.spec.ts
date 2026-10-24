import type { FullResult, TestCase, TestError, TestResult } from '@playwright/test/reporter';
import { describe, expect, it, vi } from 'vitest';
import config from '../playwright.trips-full-stack.config';
import SafeTripsReporter from './safe-reporter';

describe('full-stack public reporter', () => {
  it('publishes only allowlisted status/stage despite credential-bearing errors and output', () => {
    const secret = 'private-password-Bearer-eyJ-token-http://127.0.0.1:8186?code=private-code';
    const output: string[] = [];
    const write = vi.spyOn(process.stdout, 'write').mockImplementation((chunk) => {
      output.push(String(chunk));
      return true;
    });
    try {
      const reporter = new SafeTripsReporter();
      (reporter.onStdOut as (chunk: string) => void)(secret);
      (reporter.onStdErr as (chunk: string) => void)(secret);
      // Runtime reporter arguments are deliberately ignored, including error stacks.
      (reporter.onError as (error: TestError) => void)({ message: secret, stack: secret });
      reporter.onTestEnd(
        { title: secret, annotations: [{ type: 'trips-safe-stage', description: secret }] } as TestCase,
        {
          status: 'failed',
          errors: [{ message: secret }],
          stdout: [secret],
          stderr: [secret],
        } as TestResult,
      );
      reporter.onTestEnd(
        { annotations: [{ type: 'trips-safe-stage', description: 'owner-login' }] } as TestCase,
        {
          status: secret,
        } as unknown as TestResult,
      );
      reporter.onEnd({ status: secret } as unknown as FullResult);
      expect(output.join('')).not.toContain(secret);
      expect(output).toEqual([
        'TRIPS_E2E runner failed; private error details suppressed.\n',
        'TRIPS_E2E failed stage=validate-owned-state\n',
        'TRIPS_E2E failed stage=owner-login\n',
        'TRIPS_E2E complete status=failed\n',
      ]);
    } finally {
      write.mockRestore();
    }
  });

  it('disables private browser artifacts and retries in the dedicated configuration', () => {
    expect(config.workers).toBe(1);
    expect(config.retries).toBe(0);
    expect(config.fullyParallel).toBe(false);
    expect(config.preserveOutput).toBe('never');
    expect(config.testMatch).toBe('trips-full-stack.spec.ts');
    expect(config.use).toMatchObject({
      baseURL: 'http://127.0.0.1:4206',
      trace: 'off',
      video: 'off',
      screenshot: 'off',
      serviceWorkers: 'block',
    });
    expect(config.use?.storageState).toBeUndefined();
    expect(config.reporter).toEqual([['./trips-full-stack/safe-reporter.ts']]);
    expect(process.env['PLAYWRIGHT_NO_COPY_PROMPT']).toBe('1');
  });
});
