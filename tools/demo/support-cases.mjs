export const supportDemoCustomer = '11111111-1111-4111-8111-111111111111';
export const supportDemoOperator = '22222222-2222-4222-8222-222222222222';
export const supportDemoOther = '33333333-3333-4333-8333-333333333333';
export const supportDemoSeedId = '00000000-0000-4000-8000-000000007000';
export const supportDemoPresets = [
  'normal',
  'support-history',
  'support-lost-reply',
  'support-early-receipt',
  'support-generic-503',
  'support-permission-loss',
  'support-expired',
  'support-order-link-unavailable',
];
const categories = [
  'Unclassified',
  'Payment',
  'Ticketing',
  'Cancellation',
  'Refund',
  'FareConditions',
  'Ancillaries',
  'Technical',
  'Other',
];
const states = ['Open', 'WaitingForCustomer', 'Resolved'];
const priorities = ['Normal', 'High'];
const guid = (value) =>
  typeof value === 'string' &&
  /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(value) &&
  value !== '00000000-0000-0000-0000-000000000000';
const text = (value, maximum) =>
  typeof value === 'string' &&
  value.trim().length > 0 &&
  [...value].length <= maximum &&
  Buffer.byteLength(value, 'utf8') <= 16384 &&
  !/[\uD800-\uDBFF](?![\uDC00-\uDFFF])|(?<![\uD800-\uDBFF])[\uDC00-\uDFFF]/u.test(value);
const revision = (value) => Number.isInteger(value) && value > 0 && value <= 2147483647;
const isRecord = (value) => value !== null && typeof value === 'object' && !Array.isArray(value);
const fields = (value, required, optional = []) =>
  isRecord(value) &&
  required.every((key) => Object.hasOwn(value, key)) &&
  Object.keys(value).every((key) => [...required, ...optional].includes(key));
function sameInput(first, second) {
  if (!isRecord(first) || !isRecord(second)) return false;
  const keys = new Set([...Object.keys(first), ...Object.keys(second)]);
  return [...keys].every((key) =>
    key === 'orderId'
      ? (first[key] ?? null) === (second[key] ?? null)
      : Object.hasOwn(first, key) && Object.hasOwn(second, key) && first[key] === second[key],
  );
}
const cursor = (value) => Buffer.from(JSON.stringify(value)).toString('base64url');
function decodeCursor(value) {
  if (!/^[A-Za-z0-9_-]{1,2048}$/.test(value)) throw new Error('Invalid cursor');
  const decoded = Buffer.from(value, 'base64url');
  if (decoded.toString('base64url') !== value) throw new Error('Invalid cursor');
  return JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(decoded));
}
function send(response, status, value, problem = false) {
  response.writeHead(status, { 'Content-Type': problem ? 'application/problem+json' : 'application/json' });
  response.end(JSON.stringify(value));
}
function reject(response, status, code) {
  send(
    response,
    status,
    { status, type: 'https://travel.local/errors/' + code, title: 'Support fixture request rejected' },
    true,
  );
}
function noDuplicateKeys(raw) {
  const containers = [];
  for (let index = 0; index < raw.length; index++) {
    const token = raw[index];
    if (token === '{') containers.push({ keys: new Set(), expectsKey: true });
    else if (token === '[') containers.push(null);
    else if (token === '}' || token === ']') containers.pop();
    else if (token === ',') {
      if (containers.at(-1)) containers.at(-1).expectsKey = true;
    } else if (token === '"') {
      const start = index++;
      while (raw[index] !== '"') {
        if (raw[index] === '\\') index += 2;
        else index++;
      }
      const container = containers.at(-1);
      if (container?.expectsKey) {
        const key = JSON.parse(raw.slice(start, index + 1));
        if (container.keys.has(key)) throw new Error('Duplicate member');
        container.keys.add(key);
        container.expectsKey = false;
      }
    }
  }
}
async function bodyOf(request, maximum = 32768) {
  let size = 0;
  const chunks = [];
  for await (const chunk of request) {
    size += chunk.length;
    if (size > maximum) throw new RangeError('Too large');
    chunks.push(chunk);
  }
  const raw = new TextDecoder('utf-8', { fatal: true }).decode(Buffer.concat(chunks));
  const value = JSON.parse(raw);
  noDuplicateKeys(raw);
  return value;
}
const principals = {
  'Bearer demo-support-customer': { id: supportDemoCustomer, operator: false },
  'Bearer demo-support-operator': { id: supportDemoOperator, operator: true },
  'Bearer demo-support-other': { id: supportDemoOther, operator: false },
};
/** Loopback test-only fixture. RAM text/auth and synchronous edits do not prove production protection or transactions. */
export function createSupportDemo(options = {}) {
  const preset = options.preset ?? 'normal';
  if (!supportDemoPresets.includes(preset)) throw new TypeError('Unknown Support fixture preset');
  const cases = new Map(),
    pending = new Set();
  let faultUsed = false;
  const now = () => new Date().toISOString();
  const live = (row) => row && !(row.dto.state === 'Resolved' && Date.parse(row.dto.purgeAt) <= Date.now());
  function apply(row, action, actor, kind, input, body) {
    row.dto.revision++;
    row.dto.updatedAt = now();
    if (kind === 'OperatorReply') row.dto.state = 'WaitingForCustomer';
    if (kind === 'CustomerReply' || kind === 'ReplyAndReopen') {
      row.dto.state = 'Open';
      row.dto.resolvedAt = null;
      row.dto.purgeAt = null;
    }
    if (kind === 'Resolve') {
      row.dto.state = 'Resolved';
      row.dto.resolvedAt = now();
      row.dto.purgeAt = new Date(Date.now() + 90 * 86400000).toISOString();
    }
    if (kind === 'SetTriage') {
      row.dto.category = input.category;
      row.dto.priority = input.priority;
      row.dto.triageSource = 'Operator';
    }
    if (body !== undefined)
      row.messages.push({
        caseId: row.dto.caseId,
        messageId: action,
        actorKind: actor.operator ? 'Operator' : 'Customer',
        body,
        writtenAtRevision: row.dto.revision,
        createdAt: now(),
      });
    const data = {
      caseId: row.dto.caseId,
      actionId: action,
      kind,
      appliedRevision: row.dto.revision,
      appliedState: row.dto.state,
      appliedAt: now(),
      messageId: body === undefined ? null : action,
    };
    row.receipts.set(action, { actor: actor.id, operator: actor.operator, input: structuredClone(input), data });
    return data;
  }
  function initial(id, actor, input, proof = null) {
    const timestamp = now();
    const row = {
      dto: {
        caseId: id,
        ownerId: actor.id,
        title: input.title,
        state: 'Open',
        revision: 0,
        category: 'Unclassified',
        priority: 'Normal',
        triageSource: 'Default',
        orderId: proof ? input.orderId : null,
        orderVerifiedAt: proof ? timestamp : null,
        orderStreamVersion: proof ? proof.version : null,
        createdAt: timestamp,
        updatedAt: timestamp,
        resolvedAt: null,
        purgeAt: null,
      },
      messages: [],
      receipts: new Map(),
    };
    apply(row, input.messageId, actor, 'Create', input, input.body);
    cases.set(id, row);
    return row;
  }
  if (options.seedHistory || ['support-history', 'support-permission-loss', 'support-expired'].includes(preset)) {
    const actor = { id: supportDemoCustomer, operator: false };
    const row = initial(supportDemoSeedId, actor, {
      messageId: '00000000-0000-4000-8000-000000007001',
      title: 'Private fictional support history',
      body: 'Private fixture message 1',
    });
    for (let number = 2; number <= 25; number++) {
      const messageId = `00000000-0000-4000-8000-${String(7000 + number).padStart(12, '0')}`;
      const body = 'Private fixture message ' + number;
      apply(row, messageId, actor, 'CustomerReply', { body, expectedRevision: number - 1, kind: 'Reply' }, body);
    }
    if (preset === 'support-expired') {
      row.dto.state = 'Resolved';
      row.dto.resolvedAt = new Date(Date.now() - 91 * 86400000).toISOString();
      row.dto.purgeAt = new Date(Date.now() - 86400000).toISOString();
    }
  }
  async function handle(request, response, url) {
    if (!/^\/api\/support(?:\/|$)/.test(url.pathname)) return false;
    response.setHeader('Cache-Control', 'no-store');
    response.setHeader('X-Travel-Demo', 'fixtures');
    const credential = request.headers.authorization;
    const principal =
      typeof credential === 'string' && Object.hasOwn(principals, credential) ? principals[credential] : null;
    if (!['127.0.0.1', '::1', '::ffff:127.0.0.1'].includes(request.socket.remoteAddress) || !principal) {
      reject(response, 401, 'Support.Unauthorized');
      return true;
    }
    const match =
      /^\/api\/support\/(operator\/)?cases(?:\/([^/]+))?(?:\/(messages|actions|classification-suggestion)(?:\/([^/]+))?)?$/.exec(
        url.pathname,
      );
    if (!match) {
      reject(response, 404, 'Support.NotFound');
      return true;
    }
    const operator = Boolean(match[1]);
    if (operator && !principal.operator) {
      reject(response, 403, 'Support.Forbidden');
      return true;
    }
    const actor = { id: principal.id, operator };
    const id = match[2]?.toLowerCase(),
      resource = match[3],
      action = match[4]?.toLowerCase();
    if ((id !== undefined && !guid(id)) || (action !== undefined && !guid(action))) {
      reject(response, 400, 'Support.RequestInvalid');
      return true;
    }
    try {
      if (request.method === 'GET' && id === undefined) {
        const filters = {};
        for (const key of ['state', 'category', 'priority']) {
          const value = url.searchParams.get(key);
          if (value !== null) {
            if (!{ state: states, category: categories, priority: priorities }[key].includes(value))
              throw new Error('Filter');
            filters[key] = value;
          }
        }
        if (
          [...url.searchParams.keys()].some((key) => !['state', 'category', 'priority', 'cursor'].includes(key)) ||
          [...url.searchParams.keys()].some((key) => url.searchParams.getAll(key).length !== 1)
        )
          throw new Error('Query');
        const rawCursor = url.searchParams.get('cursor'),
          after = rawCursor ? decodeCursor(rawCursor) : null;
        if (
          after &&
          (!fields(after, ['v', 'kind', 'filters', 'createdAt', 'caseId']) ||
            after.v !== 1 ||
            after.kind !== 'cases' ||
            !sameInput(after.filters, filters) ||
            !guid(after.caseId) ||
            typeof after.createdAt !== 'string' ||
            !Number.isFinite(Date.parse(after.createdAt)))
        )
          throw new Error('Cursor');
        const rows = [...cases.values()]
          .filter(live)
          .map((row) => row.dto)
          .filter(
            (row) =>
              (operator || row.ownerId === actor.id) &&
              Object.entries(filters).every(([key, value]) => row[key] === value),
          )
          .sort(
            (first, second) =>
              second.createdAt.localeCompare(first.createdAt) || second.caseId.localeCompare(first.caseId),
          )
          .filter(
            (row) =>
              !after ||
              row.createdAt < after.createdAt ||
              (row.createdAt === after.createdAt && row.caseId < after.caseId),
          );
        const items = rows.slice(0, 20),
          last = items.at(-1);
        send(response, 200, {
          items,
          nextCursor:
            rows.length > 20
              ? cursor({ v: 1, kind: 'cases', filters, createdAt: last.createdAt, caseId: last.caseId })
              : null,
        });
        return true;
      }
      const row = cases.get(id),
        owned = live(row) && (operator || row.dto.ownerId === actor.id);
      // This standalone demo always returns Disabled. It cannot call a model or mutate S1.
      if (resource === 'classification-suggestion') {
        if (!operator || action !== undefined || request.method === 'GET') {
          reject(response, 404, 'Support.NotFound');
          return true;
        }
        if (
          request.method !== 'POST' ||
          url.search !== '' ||
          !id ||
          !/^application\/json(?:\s*;|$)/i.test(request.headers['content-type'] ?? '')
        ) {
          reject(response, 400, 'Support.RequestInvalid');
          return true;
        }
        if (!owned) {
          reject(response, 404, 'Support.NotFound');
          return true;
        }
        const input = await bodyOf(request, 1024);
        if (
          !fields(input, ['requestId', 'expectedRevision']) ||
          !guid(input.requestId) ||
          !/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(input.requestId) ||
          !revision(input.expectedRevision)
        )
          throw new Error('Input');
        if (!live(row)) {
          reject(response, 404, 'Support.NotFound');
          return true;
        }
        if (row.dto.revision !== input.expectedRevision || !['Open', 'WaitingForCustomer'].includes(row.dto.state)) {
          reject(response, 409, 'Support.RevisionConflict');
          return true;
        }
        const generated = Date.now();
        send(response, 200, {
          caseId: id,
          requestId: input.requestId.toLowerCase(),
          capturedRevision: row.dto.revision,
          generatedAt: new Date(generated).toISOString(),
          expiresAt: new Date(generated + 120000).toISOString(),
          status: 'Disabled',
          reason: 'Disabled',
          category: null,
          queue: null,
          servingProfileId: null,
          questionVersion: null,
          urgency: {
            kind: 'Unknown',
            nextScheduledDepartureAt: null,
            observedAt: null,
            streamVersion: null,
            ruleVersion: 'support-urgency-v1',
          },
        });
        return true;
      }
      if (request.method === 'GET') {
        if (!owned) {
          reject(response, 404, 'Support.NotFound');
          return true;
        }
        if (resource === 'messages' && action === undefined) {
          if (
            [...url.searchParams.keys()].some((key) => key !== 'cursor') ||
            url.searchParams.getAll('cursor').length > 1
          )
            throw new Error('Query');
          const rawCursor = url.searchParams.get('cursor'),
            after = rawCursor ? decodeCursor(rawCursor) : null;
          if (
            after &&
            (!fields(after, ['v', 'kind', 'caseId', 'revision', 'messageId']) ||
              after.v !== 1 ||
              after.kind !== 'messages' ||
              after.caseId !== id ||
              !revision(after.revision) ||
              !guid(after.messageId))
          )
            throw new Error('Cursor');
          const rows = row.messages.filter(
            (message) =>
              !after ||
              message.writtenAtRevision > after.revision ||
              (message.writtenAtRevision === after.revision && message.messageId > after.messageId),
          );
          const items = rows.slice(0, 20),
            last = items.at(-1);
          send(response, 200, {
            caseId: id,
            items,
            nextCursor:
              rows.length > 20
                ? cursor({
                    v: 1,
                    kind: 'messages',
                    caseId: id,
                    revision: last.writtenAtRevision,
                    messageId: last.messageId,
                  })
                : null,
          });
          return true;
        }
        if (url.search !== '') throw new Error('Query');
        if (resource === 'actions' && action) {
          const receipt = row.receipts.get(action);
          if (!receipt || receipt.actor !== actor.id || receipt.operator !== operator)
            reject(response, 404, 'Support.NotFound');
          else send(response, 200, receipt.data);
          return true;
        }
        if (resource === undefined) {
          send(response, 200, row.dto);
          return true;
        }
        reject(response, 404, 'Support.NotFound');
        return true;
      }
      if (
        request.method !== 'PUT' ||
        url.search !== '' ||
        !id ||
        !/^application\/json(?:\s*;|$)/i.test(request.headers['content-type'] ?? '')
      ) {
        reject(response, 400, 'Support.RequestInvalid');
        return true;
      }
      if ((operator && resource === undefined) || (!operator && resource === 'actions')) {
        reject(response, 404, 'Support.NotFound');
        return true;
      }
      if (resource !== undefined && !owned) {
        reject(response, 404, 'Support.NotFound');
        return true;
      }
      const input = await bodyOf(request);
      let kind,
        mutationId = action;
      if (resource === undefined) {
        if (
          !fields(input, ['messageId', 'title', 'body'], ['orderId']) ||
          !guid(input.messageId) ||
          !text(input.title, 120) ||
          !text(input.body, 4000) ||
          (input.orderId !== undefined && input.orderId !== null && !guid(input.orderId))
        )
          throw new Error('Input');
        input.messageId = input.messageId.toLowerCase();
        if (input.orderId !== undefined && input.orderId !== null) input.orderId = input.orderId.toLowerCase();
        kind = 'Create';
        mutationId = input.messageId;
      } else if (resource === 'messages' && action) {
        if (
          !fields(input, ['body', 'expectedRevision', 'kind']) ||
          !text(input.body, 4000) ||
          !revision(input.expectedRevision) ||
          !['Reply', 'ReplyAndReopen'].includes(input.kind) ||
          (operator && input.kind !== 'Reply')
        )
          throw new Error('Input');
        kind = operator ? 'OperatorReply' : input.kind === 'ReplyAndReopen' ? 'ReplyAndReopen' : 'CustomerReply';
      } else if (resource === 'actions' && operator && action) {
        if (input.kind === 'Resolve') {
          if (!fields(input, ['kind', 'expectedRevision']) || !revision(input.expectedRevision))
            throw new Error('Input');
          kind = 'Resolve';
        } else if (input.kind === 'SetTriage') {
          if (
            !fields(input, ['kind', 'expectedRevision', 'category', 'priority']) ||
            !revision(input.expectedRevision) ||
            !categories.includes(input.category) ||
            !priorities.includes(input.priority)
          )
            throw new Error('Input');
          kind = 'SetTriage';
        } else throw new Error('Input');
      } else {
        reject(response, 404, 'Support.NotFound');
        return true;
      }
      if (row) {
        if (!owned) {
          reject(response, 404, 'Support.NotFound');
          return true;
        }
        const previous = row.receipts.get(mutationId);
        if (previous) {
          if (
            previous.actor !== actor.id ||
            previous.operator !== operator ||
            previous.data.kind !== kind ||
            !sameInput(previous.input, input)
          )
            reject(response, 409, 'Support.ActionConflict');
          else send(response, 200, previous.data);
          return true;
        }
        if (kind === 'Create') {
          reject(response, 409, 'Support.ActionConflict');
          return true;
        }
      }
      if (pending.has(id + ':' + mutationId)) {
        reject(response, 409, 'Support.ActionInFlight');
        return true;
      }
      if (kind === 'Create') {
        let proof = null;
        if (input.orderId) {
          proof =
            preset === 'support-order-link-unavailable'
              ? { kind: 'unavailable' }
              : (options.orderAccess?.(input.orderId, actor.id) ?? null);
          if (proof?.kind === 'unavailable') {
            reject(response, 503, 'Support.OrderLinkUnavailable');
            return true;
          }
          if (!proof || !revision(proof.version)) {
            reject(response, 404, 'Support.OrderLinkNotFound');
            return true;
          }
        }
        const created = initial(id, actor, input, proof);
        send(response, 201, created.receipts.get(mutationId).data);
        return true;
      }
      if (
        row.dto.revision !== input.expectedRevision ||
        (kind === 'ReplyAndReopen' && row.dto.state !== 'Resolved') ||
        (['CustomerReply', 'OperatorReply', 'Resolve'].includes(kind) && row.dto.state === 'Resolved')
      ) {
        reject(response, 409, 'Support.RevisionConflict');
        return true;
      }
      if (!faultUsed && preset === 'support-permission-loss' && operator) {
        faultUsed = true;
        reject(response, 403, 'Support.Forbidden');
        return true;
      }
      if (!faultUsed && preset === 'support-generic-503' && kind === 'CustomerReply') {
        faultUsed = true;
        reject(response, 503, 'Support.Unavailable');
        return true;
      }
      if (!faultUsed && ['support-lost-reply', 'support-early-receipt'].includes(preset) && kind === 'CustomerReply') {
        faultUsed = true;
        if (preset === 'support-early-receipt') {
          pending.add(id + ':' + mutationId);
          setTimeout(() => {
            if (live(row) && row.dto.revision === input.expectedRevision)
              apply(row, mutationId, actor, kind, input, input.body);
            pending.delete(id + ':' + mutationId);
          }, options.receiptDelayMs ?? 1500);
        } else apply(row, mutationId, actor, kind, input, input.body);
        response.destroy();
        return true;
      }
      send(response, 200, apply(row, mutationId, actor, kind, input, resource === 'messages' ? input.body : undefined));
      return true;
    } catch (error) {
      reject(
        response,
        error instanceof RangeError ? 413 : 400,
        error instanceof RangeError ? 'Support.RequestTooLarge' : 'Support.RequestInvalid',
      );
      return true;
    }
  }
  return { handle };
}
