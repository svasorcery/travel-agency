import { createHash } from 'node:crypto';
import { expect, type Page, test } from '@playwright/test';
import { create, isolated } from './trips-fixture';

const processingPolicyVersion = createHash('sha256')
  .update(JSON.stringify(['anthropic', 'fictional-model', 'trips-brief-ru-v1']))
  .digest('hex');
async function setup(page: Page) {
  const fixture = await isolated(page, true);
  const requests: { method: string; body: unknown }[] = [];
  let configured = true;
  await page.route('**/api/trips/brief-*', async (route) => {
    const req = route.request(),
      body = req.method() === 'POST' ? req.postDataJSON() : null;
    requests.push({ method: req.method(), body });
    if (req.method() === 'GET') {
      await route.fulfill({
        status: 200,
        headers: { 'Cache-Control': 'no-store' },
        contentType: 'application/json',
        body: JSON.stringify({
          candidateVersion: 1,
          status: configured ? 'configured' : 'unconfigured',
          processorId: configured ? 'anthropic' : null,
          modelId: configured ? 'fictional-model' : null,
          processingPolicyVersion: configured ? processingPolicyVersion : null,
        }),
      });
      return;
    }
    expect(Object.keys(body).sort()).toEqual([
      'locale',
      'processingPolicyVersion',
      'referenceZone',
      'requestId',
      'text',
    ]);
    expect(body.processingPolicyVersion).toBe(processingPolicyVersion);
    const fields = Object.fromEntries(
      ['destinationId', 'startDate', 'endDate', 'durationDays', 'adults', 'interests', 'pace'].map((key) => [
        key,
        { state: 'missing', value: null, evidence: [], alternatives: [] },
      ]),
    );
    fields['destinationId'] = {
      state: 'extracted',
      value: 'saint-petersburg',
      evidence: [{ start: 0, end: 5 }],
      alternatives: [],
    } as never;
    fields['durationDays'] = {
      state: 'extracted',
      value: 3,
      evidence: [{ start: 0, end: 5 }],
      alternatives: [],
    } as never;
    fields['adults'] = {
      state: 'ambiguous',
      value: null,
      evidence: [{ start: 0, end: 5 }],
      alternatives: [2],
    } as never;
    await route.fulfill({
      status: 200,
      headers: { 'Cache-Control': 'no-store' },
      contentType: 'application/json',
      body: JSON.stringify({
        candidateVersion: 1,
        requestId: body.requestId,
        processingPolicyVersion,
        referenceInstant: '2026-10-27T00:00:00Z',
        referenceZone: body.referenceZone,
        referenceLocalDate: body.referenceZone ? '2026-10-27' : null,
        expiresAt: '2026-10-27T00:10:00Z',
        fields,
        issues: [],
      }),
    });
  });
  const id = await create(page, true);
  return { fixture, id, requests, disable: () => (configured = false) };
}
async function extract(page: Page) {
  await page.getByRole('button', { name: 'Проверить доступность разбора' }).click();
  await expect(page.getByLabel('Текст пожеланий')).toBeEnabled();
  await page.getByLabel('Текст пожеланий').fill('Питер на три дня, нас двое <img src=x onerror=alert(1)>');
  await page.getByRole('button', { name: 'Разобрать текст', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Проверьте разбор', exact: true })).toBeVisible();
}
test('FICTIONAL brief review transfers selected correction only; Apply and Save remain explicit', async ({ page }) => {
  const { fixture, id, requests } = await setup(page);
  const baseline = structuredClone(fixture.plans.get(id)!.plan);
  await extract(page);
  await expect(page.locator('[data-transfer]')).toHaveCount(5);
  for (const checkbox of await page.locator('[data-transfer]').all()) await expect(checkbox).not.toBeChecked();
  await expect(page.locator('#brief-start')).toHaveValue('');
  await expect(page.locator('#brief-end')).toHaveValue('');
  await expect(page.locator('form form')).toHaveCount(0);
  await expect(page.locator('img[src=x]')).toHaveCount(0);
  await page.getByLabel('Уточнить взрослых').fill('3');
  await page.getByLabel('Перенести взрослых', { exact: true }).check();
  await page.getByRole('button', { name: 'Подтвердить перенос в форму' }).click();
  await expect(page.getByLabel('Взрослых', { exact: true })).toHaveValue('3');
  await expect(page.getByLabel('Город поездки', { exact: true })).toHaveValue('moscow');
  expect(fixture.plans.get(id)!.plan).toEqual(baseline);
  expect(fixture.calls.filter((c) => c.method === 'PUT')).toHaveLength(0);
  expect(requests.filter((c) => c.method === 'POST')).toHaveLength(1);
  await expect(page.getByRole('button', { name: 'Предложить прогулочный маршрут' })).toBeDisabled();
  await page.getByRole('button', { name: 'Применить параметры к календарю' }).click();
  expect(fixture.calls.filter((c) => c.method === 'PUT')).toHaveLength(0);
  await page.getByRole('button', { name: 'Сохранить', exact: true }).click();
  await expect.poll(() => fixture.calls.filter((c) => c.method === 'PUT').length).toBe(1);
  expect(fixture.plans.get(id)!.plan.brief.adults).toBe(3);
  expect(fixture.plans.get(id)!.plan.brief.totalBudget).toBe('15000.00');
});
test('FICTIONAL populated calendar retains existing destination guard and logout clears review', async ({ page }) => {
  const { fixture } = await setup(page);
  await page.getByRole('button', { name: 'Добавить блок' }).first().click();
  await page.getByLabel('Название блока', { exact: true }).first().fill('Личная заметка');
  await page.getByRole('button', { name: 'Сохранить', exact: true }).click();
  await expect.poll(() => fixture.calls.filter((c) => c.method === 'PUT').length).toBe(1);
  await extract(page);
  await page.getByLabel('Перенести город', { exact: true }).check();
  await page.getByRole('button', { name: 'Подтвердить перенос в форму' }).click();
  await expect(page.getByLabel('Город поездки', { exact: true })).toHaveValue('saint-petersburg');
  await page.getByRole('button', { name: 'Применить параметры к календарю' }).click();
  await expect(page.getByLabel('Название блока', { exact: true })).toHaveValue('Личная заметка');
  expect(fixture.calls.filter((c) => c.method === 'PUT')).toHaveLength(1);
  await extract(page);
  await page.getByRole('button', { name: 'Выйти', exact: true }).click();
  await expect(page.getByLabel('Текст пожеланий')).toHaveCount(0);
  await expect(page.getByText('Питер на три дня, нас двое <img src=x onerror=alert(1)>', { exact: true })).toHaveCount(
    0,
  );
});
test('FICTIONAL capability revoked at confirmation leaves manual form unchanged', async ({ page }) => {
  const { fixture, id, disable, requests } = await setup(page);
  await extract(page);
  await page.getByLabel('Перенести город', { exact: true }).check();
  disable();
  await page.getByRole('button', { name: 'Подтвердить перенос в форму' }).click();
  await expect(page.getByRole('heading', { name: 'Проверьте разбор', exact: true })).toHaveCount(0);
  await expect(page.getByLabel('Город поездки', { exact: true })).toHaveValue('moscow');
  expect(requests.filter((c) => c.method === 'POST')).toHaveLength(1);
  expect(fixture.plans.get(id)!.plan.brief.destinationId).toBe('moscow');
  await expect(page.getByLabel('Название плана', { exact: true })).toBeEnabled();
});

test('FICTIONAL raw private text warns before leaving a clean saved plan and explicit discard clears it', async ({
  page,
}) => {
  const { fixture, requests } = await setup(page);
  await page.getByRole('button', { name: 'Проверить доступность разбора' }).click();
  await page.getByLabel('Текст пожеланий').fill('Частное пожелание без отправки');
  expect(requests.filter((call) => call.method === 'POST')).toHaveLength(0);
  const beforeUnloadPrevented = await page.evaluate(() => {
    const event = new Event('beforeunload', { cancelable: true });
    window.dispatchEvent(event);
    return event.defaultPrevented;
  });
  expect(beforeUnloadPrevented).toBe(true);
  let discard = false;
  const decisions: string[] = [];
  page.on('dialog', async (dialog) => {
    decisions.push(dialog.message());
    if (discard) await dialog.accept();
    else await dialog.dismiss();
  });
  await page.getByRole('button', { name: 'Новый план', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Ваш черновик', exact: true })).toBeVisible();
  await expect(page.getByLabel('Текст пожеланий')).toHaveValue('Частное пожелание без отправки');
  discard = true;
  await page.getByRole('button', { name: 'Новый план', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Основа нового плана', exact: true })).toBeVisible();
  await expect(page.getByLabel('Текст пожеланий')).toHaveValue('');
  expect(decisions).toHaveLength(2);
  expect(fixture.calls.filter((call) => call.method === 'PUT')).toHaveLength(0);
  expect(requests.filter((call) => call.method === 'POST')).toHaveLength(0);
});
