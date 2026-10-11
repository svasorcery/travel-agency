export const orderLivePresets = ['order-live-ticket', 'order-live-report', 'order-live-refund'];
export function fictionalOrderLiveId(preset) {
  const index = orderLivePresets.indexOf(preset);
  if (index < 0) throw new TypeError('Unknown fictional live preset');
  return `00000000-0000-4000-8000-00000000150${index + 1}`;
}

/** Loopback fictional producer. Only a startup schedule changes retained demo facts. */
export function createOrderEventsDemo(orders, quotes, fixtureOffer, preset = null, delayMs = 1500) {
  if (preset !== null && !orderLivePresets.includes(preset)) throw new TypeError('Unknown fictional live preset');
  const connections = new Map();
  let schedule = null;
  let started = false;
  if (preset) {
    const id = fictionalOrderLiveId(preset);
    const now = new Date().toISOString();
    const offer = { ...structuredClone(fixtureOffer), totalAmount: 100, currency: 'GBP' };
    orders.set(id, {
      offer,
      version: 3,
      liveScenario: true,
      status: preset === 'order-live-ticket' ? 'Confirmed' : preset === 'order-live-report' ? 'Ticketed' : 'Cancelled',
      bookedAt: now,
      ticketedAt: preset === 'order-live-report' ? now : null,
      ticketNumbers: preset === 'order-live-report' ? ['DEMO-TKT-LIVE'] : [],
      cancelledAt: preset === 'order-live-refund' ? now : null,
      customerRefund:
        preset === 'order-live-refund'
          ? {
              revision: 1,
              state: 'Unknown',
              amount: { amount: '80', currency: 'GBP' },
              destination: 'OriginalFictionalPaymentMethod',
              source: 'None',
              reason: 'OutcomeUnknown',
              observedAt: now,
            }
          : null,
    });
    quotes.set(id, {
      offer,
      binding: { passengerCount: 1, slots: [{ bookingPassengerId: '00000000-0000-4000-8000-000000001599' }] },
    });
  }
  const publish = (id, type = 'OrderChanged') => {
    const order = orders.get(id);
    if (!order) return;
    const body = JSON.stringify({
      type,
      orderId: id,
      payload: {},
      at: new Date().toISOString(),
      streamVersion: order.version ?? 3,
    });
    for (const response of connections.get(id) ?? []) {
      if (!response.write(`event: ${type}\ndata: ${body}\n\n`)) response.end();
    }
  };
  const start = (id) => {
    if (!preset || started || id !== fictionalOrderLiveId(preset)) return;
    started = true;
    schedule = setTimeout(() => {
      const order = orders.get(id);
      if (!order) return;
      const now = new Date().toISOString();
      if (preset === 'order-live-ticket') {
        order.status = 'Ticketed';
        order.ticketedAt = now;
        order.ticketNumbers = ['DEMO-TKT-LIVE'];
      }
      if (preset === 'order-live-report')
        order.supplierReport = { state: 'Unverified', source: 'SignedWebhook', observedAt: now, cancelledAt: null };
      if (preset === 'order-live-refund')
        Object.assign(order.customerRefund, {
          state: 'Sent',
          source: 'FictionalGateway',
          reason: 'None',
          revision: 2,
          observedAt: now,
        });
      order.version++;
      publish(id, preset === 'order-live-ticket' ? 'OrderTicketed' : 'OrderChanged');
    }, delayMs);
    schedule.unref();
  };
  const close = () => {
    if (schedule) clearTimeout(schedule);
    for (const responses of connections.values()) for (const response of responses) response.end();
    connections.clear();
  };
  return {
    publish,
    handle(request, response, url) {
      const route = /^\/events\/flights\/orders\/([0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12})$/i.exec(url.pathname);
      if (!route) return false;
      const id = route[1].toLowerCase();
      const denied = request.method !== 'GET' || request.headers.authorization !== undefined || url.search !== '';
      if (denied || !orders.has(id)) {
        response.writeHead(denied ? 400 : 404);
        response.end();
        return true;
      }
      response.writeHead(200, {
        'Content-Type': 'text/event-stream',
        'Cache-Control': 'no-store',
        'X-Accel-Buffering': 'no',
        'Access-Control-Allow-Origin': 'http://127.0.0.1:4201',
        'X-Travel-Demo': 'fixtures',
      });
      if (!connections.has(id)) connections.set(id, new Set());
      connections.get(id).add(response);
      response.write(': connected\n\n');
      const heartbeat = setInterval(() => {
        if (!response.write(':\n\n')) response.end();
      }, 15_000);
      heartbeat.unref();
      response.once('close', () => {
        clearInterval(heartbeat);
        connections.get(id)?.delete(response);
        if (connections.get(id)?.size === 0) connections.delete(id);
      });
      start(id);
      return true;
    },
    attach(server) {
      const originalClose = server.close.bind(server);
      server.close = (callback) => {
        close();
        return originalClose(callback);
      };
      return server;
    },
  };
}
