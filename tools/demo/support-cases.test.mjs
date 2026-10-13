import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { createServer } from 'node:http';
import { test } from 'node:test';
import { createSupportDemo, supportDemoCustomer, supportDemoSeedId } from './support-cases.mjs';

const customer = 'demo-support-customer',
  operator = 'demo-support-operator',
  other = 'demo-support-other';
async function fixture(options = {}) {
  const support = createSupportDemo(options);
  const server = createServer(async (req, res) => {
    if (!(await support.handle(req, res, new URL(req.url, 'http://127.0.0.1')))) {
      res.writeHead(404);
      res.end();
    }
  });
  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  const origin = 'http://127.0.0.1:' + server.address().port;
  const call = (path, token = customer, body, method = body === undefined ? 'GET' : 'PUT') =>
    fetch(origin + '/api/support/' + path, {
      method,
      headers: { Authorization: 'Bearer ' + token, 'Content-Type': 'application/json' },
      ...(body === undefined ? {} : { body: JSON.stringify(body) }),
    });
  return { call, origin, close: () => new Promise((resolve) => server.close(resolve)) };
}
async function create(call, title = 'Fictional question', orderId) {
  const caseId = randomUUID(),
    messageId = randomUUID(),
    body = { messageId, title, body: 'Private fixture message', ...(orderId ? { orderId } : {}) };
  const response = await call('cases/' + caseId, customer, body);
  assert.equal(response.status, 201);
  return { caseId, messageId, body, receipt: await response.json() };
}
test('fictional Support full lifecycle, strict actor receipt and exact replay use one revision', async () => {
  const f = await fixture();
  try {
    const { caseId, messageId, body } = await create(f.call);
    assert.equal((await f.call('cases/' + caseId, customer, body)).status, 200);
    assert.equal((await f.call('cases/' + caseId, customer, { ...body, title: 'Changed' })).status, 409);
    assert.equal((await f.call('cases/' + caseId, other)).status, 404);
    assert.equal((await f.call('operator/cases/' + caseId, customer)).status, 403);
    assert.equal((await f.call('operator/cases/' + caseId + '/actions/' + messageId, operator)).status, 404);
    const reply = randomUUID();
    assert.equal(
      (
        await f.call('operator/cases/' + caseId + '/messages/' + reply, operator, {
          body: 'Fictional operator reply',
          expectedRevision: 1,
          kind: 'Reply',
        })
      ).status,
      200,
    );
    const triage = randomUUID();
    assert.equal(
      (
        await f.call('operator/cases/' + caseId + '/actions/' + triage, operator, {
          kind: 'SetTriage',
          expectedRevision: 2,
          category: 'Ticketing',
          priority: 'High',
        })
      ).status,
      200,
    );
    const resolve = randomUUID();
    assert.equal(
      (
        await f.call('operator/cases/' + caseId + '/actions/' + resolve, operator, {
          kind: 'Resolve',
          expectedRevision: 3,
        })
      ).status,
      200,
    );
    assert.equal(
      (
        await f.call('cases/' + caseId + '/messages/' + randomUUID(), customer, {
          body: 'Ordinary reply',
          expectedRevision: 4,
          kind: 'Reply',
        })
      ).status,
      409,
    );
    const reopen = randomUUID();
    assert.equal(
      (
        await f.call('cases/' + caseId + '/messages/' + reopen, customer, {
          body: 'Question remains',
          expectedRevision: 4,
          kind: 'ReplyAndReopen',
        })
      ).status,
      200,
    );
    const row = await (await f.call('cases/' + caseId)).json();
    assert.equal(row.revision, 5);
    assert.equal(row.state, 'Open');
    assert.equal(row.triageSource, 'Operator');
    assert.equal(row.ownerId, supportDemoCustomer);
    const receipt = await (await f.call('cases/' + caseId + '/actions/' + messageId)).json();
    assert.equal(receipt.appliedRevision, 1);
    assert.equal(receipt.kind, 'Create');
    assert.deepEqual(
      Object.keys(receipt).sort(),
      ['actionId', 'appliedAt', 'appliedRevision', 'appliedState', 'caseId', 'kind', 'messageId'].sort(),
    );
  } finally {
    await f.close();
  }
});
test('fictional conversation keyset pages retain >20 messages and direct old actor receipts', async () => {
  const f = await fixture({ preset: 'support-history' });
  try {
    const page = await (await f.call('cases/' + supportDemoSeedId + '/messages')).json();
    assert.equal(page.items.length, 20);
    assert.ok(page.nextCursor);
    const next = await (await f.call('cases/' + supportDemoSeedId + '/messages?cursor=' + page.nextCursor)).json();
    assert.equal(next.items.length, 5);
    assert.equal(new Set([...page.items, ...next.items].map((row) => row.messageId)).size, 25);
    const receipt = await (await f.call('cases/' + supportDemoSeedId + '/actions/' + page.items[0].messageId)).json();
    assert.equal(receipt.appliedRevision, 1);
    assert.ok(!JSON.stringify(receipt).includes('Private'));
    assert.equal(
      (
        await f.call(
          'cases/' +
            supportDemoSeedId +
            '/messages?cursor=' +
            Buffer.from(JSON.stringify({ owner: 'ignored' })).toString('base64url'),
        )
      ).status,
      400,
    );
  } finally {
    await f.close();
  }
});
test('fictional auth refuses real/public tokens before reading text and keeps no-store', async () => {
  const f = await fixture({ preset: 'support-history' });
  try {
    for (const token of ['real-jwt', '', other]) {
      const response = await f.call('cases/' + supportDemoSeedId, token);
      assert.equal(response.status, token === other ? 404 : 401);
      assert.equal(response.headers.get('cache-control'), 'no-store');
      assert.ok(!(await response.text()).includes('Private'));
    }
  } finally {
    await f.close();
  }
});
test('fictional explicit order-link errors occur before writes and never silently unlink', async () => {
  let lookups = 0;
  const f = await fixture({
    orderAccess: () => {
      lookups++;
      return { kind: 'unavailable' };
    },
  });
  try {
    await create(f.call);
    assert.equal(lookups, 0);
    const id = randomUUID(),
      messageId = randomUUID();
    const response = await f.call('cases/' + id, customer, {
      messageId,
      title: 'Keep draft',
      body: 'Private draft',
      orderId: randomUUID(),
    });
    assert.equal(response.status, 503);
    assert.equal((await response.json()).type, 'https://travel.local/errors/Support.OrderLinkUnavailable');
    assert.equal((await f.call('cases/' + id)).status, 404);
    assert.equal((await f.call('cases/' + id + '/actions/' + messageId)).status, 404);
  } finally {
    await f.close();
  }
});
test('fictional lost-response commits once; receipt absence while in flight is not NoEffect', async () => {
  const f = await fixture({ preset: 'support-early-receipt', receiptDelayMs: 100 });
  try {
    const { caseId } = await create(f.call);
    const action = randomUUID(),
      body = { body: 'Lost reply', expectedRevision: 1, kind: 'Reply' };
    await assert.rejects(f.call('cases/' + caseId + '/messages/' + action, customer, body));
    assert.equal((await f.call('cases/' + caseId + '/actions/' + action)).status, 404);
    await new Promise((resolve) => setTimeout(resolve, 125));
    const receipt = await (await f.call('cases/' + caseId + '/actions/' + action)).json();
    assert.equal(receipt.appliedRevision, 2);
    assert.equal((await f.call('cases/' + caseId + '/messages/' + action, customer, body)).status, 200);
    const messages = await (await f.call('cases/' + caseId + '/messages')).json();
    assert.equal(messages.items.length, 2);
  } finally {
    await f.close();
  }
});
test('fictional expired cases disclose no content or receipt and cannot reopen', async () => {
  const f = await fixture({ preset: 'support-expired' });
  try {
    for (const suffix of ['', '/messages', '/actions/00000000-0000-4000-8000-000000007001']) {
      assert.equal((await f.call('cases/' + supportDemoSeedId + suffix)).status, 404);
    }
    assert.equal(
      (
        await f.call('cases/' + supportDemoSeedId + '/messages/' + randomUUID(), customer, {
          body: 'Cannot reopen',
          expectedRevision: 25,
          kind: 'ReplyAndReopen',
        })
      ).status,
      404,
    );
  } finally {
    await f.close();
  }
});
test('fictional strict payload validation rejects unknown fields, duplicate JSON, invalid text and revisions', async () => {
  const f = await fixture();
  try {
    for (const body of [
      { messageId: randomUUID(), title: 'Valid', body: 'Text', ownerId: supportDemoCustomer },
      { messageId: randomUUID(), title: 'Valid', body: '\ud800' },
      { messageId: randomUUID(), title: ' '.repeat(5), body: 'Text' },
    ])
      assert.equal((await f.call('cases/' + randomUUID(), customer, body)).status, 400);
    const raw = '{"messageId":"' + randomUUID() + '","title":"First","title":"Second","body":"Text"}';
    const response = await fetch(f.origin + '/api/support/cases/' + randomUUID(), {
      method: 'PUT',
      headers: { Authorization: 'Bearer ' + customer, 'Content-Type': 'application/json' },
      body: raw,
    });
    assert.equal(response.status, 400);
  } finally {
    await f.close();
  }
});
test('raw prototype property credentials are not fixture principals', async () => {
  const f = await fixture({ preset: 'support-history' });
  try {
    for (const authorization of ['__proto__', 'constructor', 'toString']) {
      const response = await fetch(f.origin + '/api/support/cases/' + supportDemoSeedId, {
        headers: { Authorization: authorization },
      });
      assert.equal(response.status, 401);
      assert.equal(response.headers.get('cache-control'), 'no-store');
      assert.ok(!(await response.text()).includes('Private'));
    }
  } finally {
    await f.close();
  }
});
test('reordered create members and uppercase GUID replay use the retained receipt', async () => {
  const f = await fixture();
  try {
    const created = await create(f.call);
    const response = await f.call('cases/' + created.caseId.toUpperCase(), customer, {
      body: created.body.body,
      title: created.body.title,
      orderId: null,
      messageId: created.messageId.toUpperCase(),
    });
    assert.equal(response.status, 200);
    assert.equal((await response.json()).appliedRevision, 1);
  } finally {
    await f.close();
  }
});
