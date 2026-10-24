import type { FullResult, TestCase, TestError, TestResult } from '@playwright/test/reporter';
import { describe, expect, it, vi } from 'vitest';
import config from '../playwright.trips-full-stack.config';
import SafeTripsReporter, { safeFailure } from './safe-reporter';

describe('full-stack public reporter', () => {
  it('distinguishes known browser body failures without exporting private error text', () => {
    const secret = 'Bearer private-token ?code=private-code';
    for (const [message, diagnostic] of [
      ['No resource with given identifier found', 'body-resource-missing'],
      ['Request content was evicted from inspector cache', 'body-cache-evicted'],
      ['Response body is unavailable', 'body-unavailable'],
      ['Protocol error (Network.getResponseBody)', 'body-protocol-error'],
      ['Target page, context or browser has been closed', 'browser-closed'],
    ]) {
      const result = safeFailure(new Error(`response.json: ${message} ${secret}`));
      expect(result.responseFailure).toBe(diagnostic);
      expect(JSON.stringify(result)).not.toContain(secret);
    }
    expect(safeFailure(new Error(secret)).responseFailure).toBeUndefined();
  });

  it('classifies ordinary failures using fixed enums without publishing messages or stacks', () => {
    const secret = 'private-password Bearer eyJ-private ?code=private-code';
    const timeout = Object.assign(new Error(secret), { name: 'TimeoutError', stack: secret });
    const assertion = Object.assign(new Error(secret), { matcherResult: { actual: secret }, stack: secret });
    expect(safeFailure(new Error(secret))).toEqual({ errorClass: 'Error', failureCategory: 'operation' });
    expect(safeFailure(new TypeError(secret))).toEqual({ errorClass: 'TypeError', failureCategory: 'operation' });
    expect(safeFailure(new SyntaxError(secret))).toEqual({ errorClass: 'SyntaxError', failureCategory: 'syntax' });
    expect(safeFailure(timeout)).toEqual({ errorClass: 'TimeoutError', failureCategory: 'timeout' });
    expect(safeFailure(assertion)).toEqual({ errorClass: 'Error', failureCategory: 'assertion' });
    expect(JSON.stringify([safeFailure(timeout), safeFailure(assertion)])).not.toContain(secret);
  });

  it('does not publish untrusted exception names or thrown objects', () => {
    const secret = 'private-password Bearer eyJ-private ?code=private-code';
    expect(safeFailure(Object.assign(new Error(secret), { name: secret }))).toEqual({
      errorClass: 'OtherError',
      failureCategory: 'unknown',
    });
    expect(safeFailure({ name: secret, message: secret, stack: secret })).toEqual({
      errorClass: 'NonError',
      failureCategory: 'unknown',
    });
  });

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
