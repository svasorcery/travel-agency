import { randomUUID } from 'node:crypto';
import type { Server } from 'node:http';
import { expect, type Page, test } from '@playwright/test';

const seedId = '00000000-0000-4000-8000-000000007000';
type ClassificationPreset = 'Disabled' | 'Candidate' | 'ManualRequired' | 'Uncertain' | 'Malformed' | 'Denied' | 'Lost';
async function isolated(
  page: Page,
  preset = 'normal',
  seedHistory = false,
  classificationPreset: ClassificationPreset = 'Disabled',
  ttl = 120000,
  classificationGate?: Promise<void>,
) {
  // Test-only loopback fixture outside Nx projects; same boundary as the Flights demo tests.
  // eslint-disable-next-line @nx/enforce-module-boundaries
  const { createDemoServer } = await import('../../../tools/demo/flights-search-api.mjs');
  const server = createDemoServer({
    supportPreset: preset,
    supportSeedHistory: seedHistory,
    supportReceiptDelayMs: 1500,
  }) as Server;
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
  const address = server.address();
  if (!address || typeof address === 'string') throw new Error('Support fixture unavailable');
  const origin = `http://127.0.0.1:${address.port}`;
  const writes: { path: string; actionId: string }[] = [];
  const logs: string[] = [];
  const classifications: { requestId: string; expectedRevision: number }[] = [];
  page.on('console', (value) => logs.push(value.text()));
  await page.route('**/*', async (route) => {
    const request = route.request(),
      url = new URL(request.url());
    expect(url.origin).toBe('http://127.0.0.1:4201');
    expect(url.username).toBe('');
    expect(url.password).toBe('');
    const token = request.headers()['authorization'];
    if (!url.pathname.startsWith('/api/') && !url.pathname.startsWith('/events/')) {
      expect(token).toBeUndefined();
      await route.continue();
      return;
    }
    expect(
      /^\/api\/support\/(?:operator\/)?cases(?:\/[0-9a-f-]{36}(?:\/(?:classification-suggestion|(?:messages|actions)(?:\/[0-9a-f-]{36})?))?)?$/.test(
        url.pathname,
      ),
    ).toBe(true);
    const classification = /^\/api\/support\/operator\/cases\/[0-9a-f-]{36}\/classification-suggestion$/.test(
      url.pathname,
    );
    expect(['GET', 'PUT', ...(classification ? ['POST'] : [])]).toContain(request.method());
    expect(['Bearer demo-support-customer', 'Bearer demo-support-operator', 'Bearer demo-support-other']).toContain(
      token,
    );
    expect([...url.searchParams.keys()].every((key) => ['cursor', 'state', 'category', 'priority'].includes(key))).toBe(
      true,
    );
    if (classification) {
      expect(request.method()).toBe('POST');
      expect(token).toBe('Bearer demo-support-operator');
      const body = request.postDataJSON();
      expect(Object.keys(body).sort()).toEqual(['expectedRevision', 'requestId']);
      expect(body.requestId).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
      expect(Number.isInteger(body.expectedRevision) && body.expectedRevision > 0).toBe(true);
      classifications.push(body);
      if (classificationGate) await classificationGate;
      if (classificationPreset === 'Lost') {
        await route.abort('failed');
        return;
      }
      if (classificationPreset === 'Denied') {
        await route.fulfill({
          status: 403,
          contentType: 'application/problem+json',
          headers: { 'Cache-Control': 'no-store' },
          body: JSON.stringify({ type: 'Support.Forbidden' }),
        });
        return;
      }
      const candidate = classificationPreset === 'Candidate' || classificationPreset === 'Malformed';
      const high = classificationPreset === 'ManualRequired';
      const time = Date.now();
      const value = {
        caseId: url.pathname.split('/')[5],
        requestId: body.requestId,
        capturedRevision: body.expectedRevision,
        generatedAt: new Date(time).toISOString(),
        expiresAt: new Date(time + ttl).toISOString(),
        status: candidate ? 'Candidate' : classificationPreset === 'Disabled' ? 'Disabled' : 'ManualRequired',
        reason: candidate
          ? 'Ready'
          : classificationPreset === 'Disabled'
            ? 'Disabled'
            : classificationPreset === 'Uncertain'
              ? 'Uncertain'
              : 'LowConfidence',
        category: candidate ? 'Refund' : null,
        queue: candidate ? 'Refund' : null,
        servingProfileId: candidate ? 'a'.repeat(64) : null,
        questionVersion: candidate ? 'support-category-v1' : null,
        urgency: {
          kind: high ? 'High' : 'Unknown',
          nextScheduledDepartureAt: high ? new Date(time + 3600000).toISOString() : null,
          observedAt: high ? new Date(time).toISOString() : null,
          streamVersion: high ? 4 : null,
          ruleVersion: 'support-urgency-v1',
        },
        ...(classificationPreset === 'Malformed' ? { debug: 'raw private fixture diagnostics' } : {}),
      };
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        headers: { 'Cache-Control': 'no-store' },
        body: JSON.stringify(value),
      });
      return;
    }
    if (request.method() === 'PUT') {
      const body = request.postDataJSON();
      writes.push({ path: url.pathname, actionId: body.messageId ?? url.pathname.split('/').at(-1)! });
    }
    try {
      const response = await fetch(origin + url.pathname + url.search, {
        method: request.method(),
        redirect: 'error',
        headers: { 'Content-Type': 'application/json', Authorization: token! },
        body: request.postData() ?? undefined,
      });
      await route.fulfill({
        status: response.status,
        contentType: response.headers.get('Content-Type') ?? 'application/json',
        headers: { 'Cache-Control': response.headers.get('Cache-Control') ?? 'no-store' },
        body: Buffer.from(await response.arrayBuffer()),
      });
    } catch {
      await route.abort('failed');
    }
  });
  return {
    origin,
    classifications,
    writes,
    logs,
    async close() {
      server.closeAllConnections();
      await new Promise<void>((resolve) => server.close(() => resolve()));
    },
  };
}
async function login(page: Page, path = '/support') {
  await page.goto(path);
  await page.getByTestId('support-login').click();
  await expect(page.getByRole('button', { name: 'Выйти', exact: true })).toBeVisible();
}
async function create(page: Page, title: string, body = 'Fixture private customer question') {
  await page.locator('#support-title').fill(title);
  await page.locator('#support-create-body').fill(body);
  await page.getByTestId('support-create').click();
  await expect(page).toHaveURL(/\/support\/cases\/[0-9a-f-]{36}$/);
  await expect(page.getByRole('heading', { name: title, exact: true })).toBeVisible();
  return page.url().split('/').at(-1)!;
}
async function privateProof(page: Page, values: string[], logs: string[]) {
  const persisted = await page.evaluate(() =>
    JSON.stringify({ local: { ...localStorage }, session: { ...sessionStorage }, history: history.state }),
  );
  for (const value of values) {
    expect(persisted).not.toContain(value);
    expect(page.url()).not.toContain(encodeURIComponent(value));
    expect(logs.join('\n')).not.toContain(value);
  }
}
test('Support customer can create with keyboard on a narrow viewport without horizontal overflow', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 812 });
  const fixture = await isolated(page);
  try {
    await login(page);
    const title = 'Keyboard fixture ' + randomUUID();
    await page.locator('#support-title').fill(title);
    await page.locator('#support-title').press('Tab');
    await expect(page.locator('#support-create-body')).toBeFocused();
    await page.locator('#support-create-body').fill('Keyboard fixture message');
    await page.locator('#support-create-body').press('Tab');
    await expect(page.getByTestId('support-create')).toBeFocused();
    await page.getByTestId('support-create').press('Enter');
    await expect(page.getByRole('heading', { name: title, exact: true })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    expect(fixture.writes).toHaveLength(1);
    await privateProof(page, [title, 'Keyboard fixture message'], fixture.logs);
  } finally {
    await fixture.close();
  }
});
test('Support customer and operator complete manual lifecycle without financial or model traffic', async ({ page }) => {
  const fixture = await isolated(page);
  const title = 'Support fixture ' + randomUUID(),
    first = '<script>Fixture plain text</script>';
  try {
    await login(page);
    const id = await create(page, title, first);
    await expect(page.getByTestId('support-message')).toHaveText(first);
    expect(await page.locator('.conversation script').count()).toBe(0);
    await page.getByRole('link', { name: 'Вход оператора', exact: true }).click();
    await page.getByTestId('support-login').click();
    await page.getByTestId('support-case-link').filter({ hasText: title }).click();
    await expect(page).toHaveURL(new RegExp('/support/operator/cases/' + id + '$'));
    await page.locator('select[name="triageCategory"]').selectOption('Ticketing');
    await page.locator('select[name="triagePriority"]').selectOption('High');
    await page.getByTestId('support-triage').click();
    await expect(page.getByText('Обращение · версия 2', { exact: true })).toBeVisible();
    await page.locator('#support-reply-body').fill('Fixture operator answer');
    await page.getByTestId('support-reply').click();
    await expect(page.getByTestId('support-message')).toHaveCount(2);
    await expect(page.getByText('Оператор Travel', { exact: true })).toBeVisible();
    await page.getByTestId('support-resolve').click();
    await expect(page.locator('.conversation .status')).toHaveText('Решено');
    await page.getByRole('link', { name: 'Клиентский вход', exact: true }).click();
    await page.getByTestId('support-login').click();
    await page.getByTestId('support-case-link').filter({ hasText: title }).click();
    await page.locator('#support-reply-body').fill('Fixture further question');
    await page.getByTestId('support-reply').click();
    await expect(page.locator('.conversation .status')).toHaveText('Открыто');
    await expect(page.getByText('Обращение · версия 5', { exact: true })).toBeVisible();
    expect(fixture.writes).toHaveLength(5);
    expect(fixture.writes.every((write) => write.path.startsWith('/api/support/'))).toBe(true);
    await privateProof(page, [title, first, 'Fixture operator answer', 'Fixture further question'], fixture.logs);
  } finally {
    await fixture.close();
  }
});
test('Support current permission denial clears same-identity private text while unresolved action survives login', async ({
  page,
}) => {
  const fixture = await isolated(page, 'support-permission-loss');
  try {
    await login(page, '/support/operator/cases/' + seedId);
    await expect(page.getByTestId('support-message')).toHaveCount(20);
    await page.locator('#support-reply-body').fill('Fixture operator draft');
    await page.getByTestId('support-reply').click();
    await expect(page.getByTestId('support-login')).toBeVisible();
    await expect(page.getByText('Private fictional support history', { exact: true })).toHaveCount(0);
    await expect(page.getByTestId('support-message')).toHaveCount(0);
    await privateProof(page, ['Private fictional support history', 'Fixture operator draft'], fixture.logs);
    await page.getByTestId('support-login').click();
    await expect(page.getByRole('heading', { name: 'Проверяем исход отправки' })).toBeVisible();
    await expect(page.locator('#support-reply-body')).toHaveValue('');
    await expect(page.getByTestId('support-reply')).toBeDisabled();
    expect(fixture.writes).toHaveLength(1);
    await page.getByTestId('support-reconcile').click();
    await expect(page.getByTestId('support-reply')).toBeDisabled();
    expect(fixture.writes).toHaveLength(1);
  } finally {
    await fixture.close();
  }
});
test('Support expired fixture content stays undisclosed and cannot be reopened', async ({ page }) => {
  const fixture = await isolated(page, 'support-expired');
  try {
    await login(page, '/support/cases/' + seedId);
    await expect(page.getByTestId('support-error')).toContainText('не найдено');
    await expect(page.getByTestId('support-message')).toHaveCount(0);
    await expect(page.locator('#support-reply-body')).toHaveCount(0);
    expect(fixture.writes).toHaveLength(0);
  } finally {
    await fixture.close();
  }
});
test('Support reserved order-link rejection keeps draft and needs an explicit fresh unlinked create', async ({
  page,
}) => {
  const fixture = await isolated(page, 'support-order-link-unavailable');
  const title = 'Fixture unlinked ' + randomUUID(),
    body = 'Fixture retained draft';
  try {
    await login(page, '/support?orderId=44444444-4444-4444-8444-444444444444');
    await page.locator('#support-title').fill(title);
    await page.locator('#support-create-body').fill(body);
    await page.getByTestId('support-create').click();
    await expect(page.getByTestId('support-create-unlinked')).toBeVisible();
    await expect(page.locator('#support-create-body')).toHaveValue(body);
    await expect(page.getByTestId('support-create')).toBeDisabled();
    expect(fixture.writes).toHaveLength(1);
    await page.getByTestId('support-create-unlinked').click();
    await expect(page).toHaveURL(/\/support\/cases\/[0-9a-f-]{36}$/);
    await expect(page.getByRole('heading', { name: title, exact: true })).toBeVisible();
    expect(fixture.writes).toHaveLength(2);
    expect(fixture.writes[1].path).not.toBe(fixture.writes[0].path);
    expect(fixture.writes[1].actionId).not.toBe(fixture.writes[0].actionId);
    await privateProof(page, [title, body], fixture.logs);
  } finally {
    await fixture.close();
  }
});
test('Support reload reads the resource and never reconstructs or sends an unknown reply draft', async ({ page }) => {
  const fixture = await isolated(page, 'support-generic-503');
  try {
    await login(page);
    await create(page, 'Fixture reload ' + randomUUID());
    await page.locator('#support-reply-body').fill('Fixture lost draft');
    await page.getByTestId('support-reply').click();
    await expect(page.getByRole('heading', { name: 'Проверяем исход отправки' })).toBeVisible();
    expect(fixture.writes).toHaveLength(2);
    await page.reload();
    await page.getByTestId('support-login').click();
    await expect(page.getByTestId('support-message')).toHaveCount(1);
    await expect(page.locator('#support-reply-body')).toHaveValue('');
    expect(fixture.writes).toHaveLength(2);
    await privateProof(page, ['Fixture lost draft'], fixture.logs);
  } finally {
    await fixture.close();
  }
});
test('Support paged absence and an old direct receipt cannot replace newer state or repeat a reply', async ({
  page,
}) => {
  const fixture = await isolated(page, 'support-lost-reply', true);
  try {
    await login(page, '/support/cases/' + seedId);
    await expect(page.getByTestId('support-message')).toHaveCount(20);
    await page.locator('#support-reply-body').fill('Fixture lost reply');
    await page.getByTestId('support-reply').click();
    await expect(page.getByRole('heading', { name: 'Проверяем исход отправки' })).toBeVisible();
    expect(fixture.writes).toHaveLength(1);
    const action = fixture.writes[0].actionId;
    const operatorReply = await fetch(
      fixture.origin + '/api/support/operator/cases/' + seedId + '/messages/' + randomUUID(),
      {
        method: 'PUT',
        headers: { Authorization: 'Bearer demo-support-operator', 'Content-Type': 'application/json' },
        body: JSON.stringify({ body: 'Fixture newer answer', expectedRevision: 26, kind: 'Reply' }),
      },
    );
    expect(operatorReply.status).toBe(200);
    await page.getByRole('button', { name: 'Обновить обращение', exact: true }).click();
    await expect(page.getByText('Обращение · версия 27', { exact: true })).toBeVisible();
    await expect(page.getByTestId('support-reply')).toBeDisabled();
    await page.getByRole('button', { name: 'Показать следующие сообщения', exact: true }).click();
    await expect(page.getByTestId('support-message')).toHaveCount(27);
    await expect(page.getByTestId('support-reply')).toBeDisabled();
    await page.getByTestId('support-reconcile').click();
    await expect(page.getByRole('heading', { name: 'Проверяем исход отправки' })).toHaveCount(0);
    await expect(page.getByText('Обращение · версия 27', { exact: true })).toBeVisible();
    expect(fixture.writes).toHaveLength(1);
    const receipt = await (
      await fetch(fixture.origin + '/api/support/cases/' + seedId + '/actions/' + action, {
        headers: { Authorization: 'Bearer demo-support-customer' },
      })
    ).json();
    expect(receipt.appliedRevision).toBe(26);
    expect(receipt).not.toHaveProperty('body');
    await privateProof(page, ['Fixture lost reply', 'Fixture newer answer'], fixture.logs);
  } finally {
    await fixture.close();
  }
});
test('Support generic 503 and negative direct receipt keep the write barrier despite readable snapshot', async ({
  page,
}) => {
  const fixture = await isolated(page, 'support-generic-503');
  try {
    await login(page);
    await create(page, 'Fixture negative ' + randomUUID());
    await page.locator('#support-reply-body').fill('Fixture unknown reply');
    await page.getByTestId('support-reply').click();
    await expect(page.getByRole('heading', { name: 'Проверяем исход отправки' })).toBeVisible();
    await page.getByTestId('support-reconcile').click();
    await expect(page.getByTestId('support-error')).toContainText('не разрешает повторную отправку');
    await page.getByRole('button', { name: 'Обновить обращение', exact: true }).click();
    await expect(page.getByTestId('support-message')).toHaveCount(1);
    await expect(page.getByTestId('support-reply')).toBeDisabled();
    expect(fixture.writes).toHaveLength(2);
  } finally {
    await fixture.close();
  }
});
test('Support early receipt 404 stays unknown until exact positive receipt', async ({ page }) => {
  const fixture = await isolated(page, 'support-early-receipt');
  try {
    await login(page);
    await create(page, 'Fixture early ' + randomUUID());
    await page.locator('#support-reply-body').fill('Fixture delayed reply');
    await page.getByTestId('support-reply').click();
    await expect(page.getByRole('heading', { name: 'Проверяем исход отправки' })).toBeVisible();
    await page.getByTestId('support-reconcile').click();
    await expect(page.getByTestId('support-error')).toContainText('не разрешает повторную отправку');
    await expect(page.getByTestId('support-reply')).toBeDisabled();
    expect(fixture.writes).toHaveLength(2);
    const path = fixture.writes[1].path.replace('/messages/', '/actions/');
    await expect
      .poll(
        async () =>
          (await fetch(fixture.origin + path, { headers: { Authorization: 'Bearer demo-support-customer' } })).status,
      )
      .toBe(200);
    await page.getByTestId('support-reconcile').click();
    await expect(page.getByRole('heading', { name: 'Проверяем исход отправки' })).toHaveCount(0);
    expect(fixture.writes).toHaveLength(2);
  } finally {
    await fixture.close();
  }
});

async function operatorCase(page: Page) {
  await login(page);
  const title = 'S2 fictional fixture ' + randomUUID();
  const id = await create(page, title);
  await expect(page.getByTestId('support-classify')).toHaveCount(0);
  await page.getByRole('link', { name: 'Вход оператора', exact: true }).click();
  await page.getByTestId('support-login').click();
  await page.getByTestId('support-case-link').filter({ hasText: title }).click();
  await expect(page.getByTestId('support-classify')).toBeEnabled();
  return { id, title };
}
test('S2 fictional candidate is explicit and prefills only category until separate manual receipt', async ({
  page,
}) => {
  const fixture = await isolated(page, 'normal', false, 'Candidate');
  try {
    const { id, title } = await operatorCase(page);
    expect(fixture.classifications).toHaveLength(0);
    const before = fixture.writes.length;
    await page.getByTestId('support-classify').click();
    await expect(page.getByTestId('support-suggestion')).toContainText('правильность не гарантируется');
    await expect(page.getByTestId('support-suggestion')).toContainText('не назначает сотрудника');
    expect(fixture.writes).toHaveLength(before);
    await page.getByTestId('support-suggestion-prefill').click();
    await expect(page.locator('select[name="triageCategory"]')).toHaveValue('Refund');
    await expect(page.locator('select[name="triagePriority"]')).toHaveValue('Normal');
    expect(fixture.writes).toHaveLength(before);
    await page.getByTestId('support-triage').click();
    await expect(page.getByText('Обращение · версия 2', { exact: true })).toBeVisible();
    expect(fixture.writes).toHaveLength(before + 1);
    const saved = await (
      await fetch(fixture.origin + '/api/support/operator/cases/' + id, {
        headers: { Authorization: 'Bearer demo-support-operator' },
      })
    ).json();
    expect(saved.category).toBe('Refund');
    expect(saved.priority).toBe('Normal');
    expect(saved.triageSource).toBe('Operator');
    expect(fixture.classifications).toHaveLength(1);
    await page.reload();
    await page.getByTestId('support-login').click();
    await expect(page.getByTestId('support-suggestion')).toHaveCount(0);
    expect(fixture.classifications).toHaveLength(1);
    await privateProof(page, [title, 'a'.repeat(64)], fixture.logs);
  } finally {
    await fixture.close();
  }
});
for (const preset of ['Disabled', 'ManualRequired', 'Uncertain', 'Malformed', 'Lost'] as const) {
  test('S2 fixture ' + preset + ' keeps manual workflow and does not automatically replay', async ({ page }) => {
    const fixture = await isolated(page, 'normal', false, preset);
    try {
      await operatorCase(page);
      await page.getByTestId('support-classify').click();
      if (preset === 'Malformed' || preset === 'Lost')
        await expect(page.getByTestId('support-classification-notice')).toBeVisible();
      else await expect(page.getByTestId('support-suggestion')).toBeVisible();
      await expect(page.getByTestId('support-suggestion-prefill')).toHaveCount(0);
      await expect(page.getByTestId('support-triage')).toBeEnabled();
      await expect(page.getByTestId('support-reply')).toBeEnabled();
      if (preset === 'ManualRequired') {
        await expect(page.getByTestId('support-suggestion')).toContainText('Рекомендуем высокий приоритет');
        await expect(page.locator('select[name="triagePriority"]')).toHaveValue('Normal');
      }
      if (preset === 'Uncertain' || preset === 'Lost') await expect(page.getByText(/могло выполниться/)).toBeVisible();
      await page.getByRole('button', { name: 'Обновить обращение', exact: true }).click();
      await expect(page.getByTestId('support-suggestion')).toHaveCount(0);
      expect(fixture.classifications).toHaveLength(1);
      await privateProof(page, ['raw private fixture diagnostics', 'a'.repeat(64)], fixture.logs);
    } finally {
      await fixture.close();
    }
  });
}
test('S2 TTL restores untouched prefill and marks corrected draft for current-case review', async ({ page }) => {
  const fixture = await isolated(page, 'normal', false, 'Candidate', 3000);
  try {
    await operatorCase(page);
    await page.getByTestId('support-classify').click();
    await page.getByTestId('support-suggestion-prefill').click();
    await expect(page.locator('select[name="triageCategory"]')).toHaveValue('Refund');
    await expect(page.getByTestId('support-suggestion')).toHaveCount(0, { timeout: 10000 });
    await expect(page.locator('select[name="triageCategory"]')).toHaveValue('Unclassified');
    await page.getByTestId('support-classify').click();
    await page.getByTestId('support-suggestion-prefill').click();
    await page.locator('select[name="triageCategory"]').selectOption('Payment');
    await expect(page.getByTestId('support-draft-review')).toBeVisible({ timeout: 10000 });
    await expect(page.locator('select[name="triageCategory"]')).toHaveValue('Payment');
    await expect(page.getByTestId('support-triage')).toBeDisabled();
    await page.getByRole('button', { name: 'Проверил ручной выбор', exact: true }).click();
    await expect(page.getByTestId('support-triage')).toBeEnabled();
    expect(fixture.writes).toHaveLength(1);
  } finally {
    await fixture.close();
  }
});
test('S2 current permission loss clears private preview and pending draft', async ({ page }) => {
  const fixture = await isolated(page, 'normal', false, 'Denied');
  try {
    const { title } = await operatorCase(page);
    await page.locator('#support-reply-body').fill('S2 private draft');
    await page.getByTestId('support-classify').click();
    await expect(page.getByTestId('support-login')).toBeVisible();
    await expect(page.getByTestId('support-suggestion')).toHaveCount(0);
    await expect(page.getByText(title, { exact: true })).toHaveCount(0);
    await privateProof(page, [title, 'S2 private draft'], fixture.logs);
    expect(fixture.classifications).toHaveLength(1);
  } finally {
    await fixture.close();
  }
});
test('S2 delayed inference cannot restore a suggestion after a same-revision refresh', async ({ page }) => {
  let release!: () => void;
  const gate = new Promise<void>((resolve) => {
    release = resolve;
  });
  const fixture = await isolated(page, 'normal', false, 'Candidate', 120000, gate);
  try {
    await operatorCase(page);
    await page.getByTestId('support-classify').click();
    await expect.poll(() => fixture.classifications.length).toBe(1);
    await expect(page.getByTestId('support-triage')).toBeEnabled();
    await page.getByRole('button', { name: 'Обновить обращение', exact: true }).click();
    await expect(page.getByTestId('support-classify')).toBeEnabled();
    const completed = page.waitForResponse((response) => response.url().endsWith('/classification-suggestion'));
    release();
    await completed;
    await expect(page.getByTestId('support-suggestion')).toHaveCount(0);
    expect(fixture.classifications).toHaveLength(1);
  } finally {
    release();
    await fixture.close();
  }
});

test('S2 edited draft requires review again after another operator raises priority at a newer revision', async ({
  page,
}) => {
  const fixture = await isolated(page, 'normal', false, 'Candidate', 3000);
  try {
    const { id } = await operatorCase(page);
    await page.getByTestId('support-classify').click();
    await page.getByTestId('support-suggestion-prefill').click();
    await page.locator('select[name="triageCategory"]').selectOption('Payment');
    await expect(page.getByTestId('support-draft-review')).toBeVisible({ timeout: 10000 });
    await page.getByRole('button', { name: 'Проверил ручной выбор', exact: true }).click();
    await expect(page.getByTestId('support-triage')).toBeEnabled();
    const otherOperator = await fetch(
      fixture.origin + '/api/support/operator/cases/' + id + '/actions/' + randomUUID(),
      {
        method: 'PUT',
        headers: { Authorization: 'Bearer demo-support-operator', 'Content-Type': 'application/json' },
        body: JSON.stringify({ kind: 'SetTriage', expectedRevision: 1, category: 'Ticketing', priority: 'High' }),
      },
    );
    expect(otherOperator.status).toBe(200);
    await page.getByRole('button', { name: 'Обновить обращение', exact: true }).click();
    await expect(page.getByText('Обращение · версия 2', { exact: true })).toBeVisible();
    await expect(page.locator('select[name="triageCategory"]')).toHaveValue('Payment');
    await expect(page.locator('select[name="triagePriority"]')).toHaveValue('Normal');
    await expect(page.getByTestId('support-draft-review')).toBeVisible();
    await expect(page.getByTestId('support-triage')).toBeDisabled();
    expect(fixture.writes).toHaveLength(1);
    const beforeReview = await (
      await fetch(fixture.origin + '/api/support/operator/cases/' + id, {
        headers: { Authorization: 'Bearer demo-support-operator' },
      })
    ).json();
    expect(beforeReview.priority).toBe('High');
    expect(beforeReview.revision).toBe(2);
    await page.getByRole('button', { name: 'Проверил ручной выбор', exact: true }).click();
    await expect(page.getByTestId('support-triage')).toBeEnabled();
    await page.getByTestId('support-triage').click();
    await expect(page.getByText('Обращение · версия 3', { exact: true })).toBeVisible();
    const saved = await (
      await fetch(fixture.origin + '/api/support/operator/cases/' + id, {
        headers: { Authorization: 'Bearer demo-support-operator' },
      })
    ).json();
    expect(saved.category).toBe('Payment');
    expect(saved.priority).toBe('Normal');
    expect(saved.triageSource).toBe('Operator');
    expect(fixture.writes).toHaveLength(2);
    expect(fixture.classifications).toHaveLength(1);
  } finally {
    await fixture.close();
  }
});
