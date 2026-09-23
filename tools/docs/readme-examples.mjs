import { readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const BEGIN = '<!-- BEGIN FLIGHTS REQUEST EXAMPLES -->';
const END = '<!-- END FLIGHTS REQUEST EXAMPLES -->';
const IDS = ['search', 'nlSearch', 'quote', 'hold', 'confirm', 'sse'];
const ROUTES = {
  search: ['POST', '/api/flights/search'],
  nlSearch: ['POST', '/api/flights/search/nl'],
  quote: ['POST', '/api/flights/orders/quote'],
  hold: ['POST', '/api/flights/orders/hold'],
  confirm: ['POST', '/api/flights/orders/confirm'],
  sse: ['GET', '/events/flights/orders/{orderId}'],
};
const TOKENS = new Set([
  'departureDate',
  'providerOfferRef',
  'aggregateId',
  'jwt',
  'holdIdempotencyKey',
  'confirmIdempotencyKey',
]);
const TITLES = {
  search: 'Search (anonymous)',
  nlSearch: 'NL-search (anonymous; requires Anthropic for a parsed result)',
  quote: 'Quote (requires a current Duffel offer reference)',
  hold: 'Hold (requires a JWT with flights:book)',
  confirm: 'Confirm (requires a JWT with flights:book)',
  sse: 'Stream order status (authenticated)',
};

function exactKeys(value, expected, label) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    throw new Error(label + ' must be an object');
  }
  const actual = Object.keys(value).sort();
  if (JSON.stringify(actual) !== JSON.stringify([...expected].sort())) {
    throw new Error(
      label + ' fields differ; expected: ' + [...expected].sort().join(', ') + '; actual: ' + actual.join(', '),
    );
  }
}

function validateTokens(value) {
  if (typeof value === 'string') {
    const withoutValidTokens = value.replace(/\{\{([^{}]+)\}\}/g, (_, token) => {
      if (!TOKENS.has(token)) throw new Error('unknown example token: ' + token);
      return '';
    });
    if (withoutValidTokens.includes('{{') || withoutValidTokens.includes('}}')) {
      throw new Error('unresolved example token');
    }
  } else if (Array.isArray(value)) {
    for (const entry of value) validateTokens(entry);
  } else if (value && typeof value === 'object') {
    for (const entry of Object.values(value)) validateTokens(entry);
  }
}

export function validateCatalog(catalog) {
  if (!Array.isArray(catalog) || catalog.length !== IDS.length) {
    throw new Error('example catalog must contain exactly six requests');
  }
  if (JSON.stringify(catalog.map((item) => item.id)) !== JSON.stringify(IDS)) {
    throw new Error('example IDs are missing, duplicated or reordered');
  }
  for (const item of catalog) {
    exactKeys(item, ['id', 'method', 'path', 'pathParameters', 'headers', 'body'], item.id);
    const [method, path] = ROUTES[item.id];
    if (item.method !== method || item.path !== path) {
      throw new Error(item.id + ' method or OpenAPI route differs');
    }
    const params = [...item.path.matchAll(/\{([A-Za-z][A-Za-z0-9]*)\}/g)].map((match) => match[1]);
    exactKeys(item.pathParameters, params, item.id + ' pathParameters');
    if (item.id === 'sse' && item.pathParameters.orderId !== '{{aggregateId}}') {
      throw new Error('SSE orderId must use the aggregateId token');
    }
    if (item.id === 'sse') {
      if (item.body !== null) throw new Error('SSE body must be null');
      exactKeys(item.headers, ['Authorization'], 'sse headers');
      if (item.headers.Authorization !== 'Bearer {{jwt}}') {
        throw new Error('SSE Authorization header is required');
      }
    } else {
      if (item.headers['Content-Type'] !== 'application/json') {
        throw new Error(item.id + ' Content-Type is required');
      }
    }
    if (item.id === 'hold' || item.id === 'confirm') {
      if (item.headers.Authorization !== 'Bearer {{jwt}}') {
        throw new Error(item.id + ' Authorization header is required');
      }
      if (
        item.headers['Idempotency-Key'] !==
        (item.id === 'hold' ? '{{holdIdempotencyKey}}' : '{{confirmIdempotencyKey}}')
      ) {
        throw new Error(item.id + ' Idempotency-Key is required');
      }
    }
    validateTokens(item);
  }
  exactKeys(
    catalog[0].body,
    ['origin', 'destination', 'departureDate', 'returnDate', 'passengerCount', 'cabinClass'],
    'search body',
  );
  if (
    catalog[0].body.departureDate !== '{{departureDate}}' ||
    catalog[0].body.returnDate !== null ||
    catalog[0].body.passengerCount !== 1
  ) {
    throw new Error('search departureDate token and passengerCount=1 are required');
  }
  if (!catalog[1].body.query.includes('{{departureDate}}')) {
    throw new Error('NL query must include the departureDate token');
  }
  exactKeys(catalog[2].body, ['providerOfferRef', 'provider'], 'quote body');
  if (catalog[2].body.providerOfferRef !== '{{providerOfferRef}}') {
    throw new Error('quote providerOfferRef token is required');
  }
  exactKeys(catalog[3].body, ['aggregateId', 'passengers'], 'hold body');
  exactKeys(catalog[4].body, ['aggregateId'], 'confirm body');
  if (catalog[3].body.aggregateId !== '{{aggregateId}}' || catalog[4].body.aggregateId !== '{{aggregateId}}') {
    throw new Error('hold and confirm aggregateId token is required');
  }
  if (!Array.isArray(catalog[3].body.passengers) || catalog[3].body.passengers.length !== 1) {
    throw new Error('hold requires one example passenger');
  }
  exactKeys(
    catalog[3].body.passengers[0],
    ['givenName', 'familyName', 'dateOfBirth', 'gender', 'email', 'phone'],
    'hold passenger',
  );
  return catalog;
}

function shellQuote(value) {
  return "'" + value.replaceAll("'", "'\\''") + "'";
}

export function renderExamples(catalog) {
  const fence = String.fromCharCode(96).repeat(3);
  const slash = String.fromCharCode(92);
  return catalog
    .map((item, index) => {
      let route = item.path;
      for (const [name, value] of Object.entries(item.pathParameters)) {
        route = route.replace('{' + name + '}', value);
      }
      const prefix = item.id === 'sse' ? 'curl --no-buffer -sS -X ' : 'curl -sS -X ';
      const lines = [prefix + item.method + ' http://localhost:5099' + route];
      for (const [name, value] of Object.entries(item.headers)) {
        lines.push('  -H ' + shellQuote(name + ': ' + value));
      }
      if (item.body !== null) {
        lines.push('  --data-binary ' + shellQuote(JSON.stringify(item.body, null, 2)));
      }
      const command = lines.join(' ' + slash + '\n');
      return '### ' + (index + 1) + '. ' + TITLES[item.id] + '\n\n' + fence + 'bash\n' + command + '\n' + fence;
    })
    .join('\n\n');
}

export function replaceExamples(readme, rendered) {
  const windowsNewlines = readme.includes('\r\n');
  const normalized = readme.replaceAll('\r\n', '\n');
  if (normalized.split(BEGIN).length !== 2 || normalized.split(END).length !== 2) {
    throw new Error('README example markers must each appear exactly once');
  }
  const begin = normalized.indexOf(BEGIN);
  const end = normalized.indexOf(END);
  if (end < begin) throw new Error('README example markers are out of order');
  const result =
    normalized.slice(0, begin + BEGIN.length) + '\n\n' + rendered.trimEnd() + '\n\n' + normalized.slice(end);
  return windowsNewlines ? result.replaceAll('\n', '\r\n') : result;
}

export function checkReadme(readme, rendered) {
  return replaceExamples(readme, rendered) === readme;
}

async function main() {
  const mode = process.argv[2];
  if (mode !== '--check' && mode !== '--write') {
    throw new Error('usage: node tools/docs/readme-examples.mjs --check|--write');
  }
  const catalogUrl = new URL('../../docs/examples/flights-requests.json', import.meta.url);
  const readmeUrl = new URL('../../README.md', import.meta.url);
  const catalog = validateCatalog(JSON.parse(await readFile(catalogUrl, 'utf8')));
  const readme = await readFile(readmeUrl, 'utf8');
  const rendered = renderExamples(catalog);
  if (mode === '--check') {
    if (!checkReadme(readme, rendered)) throw new Error('README examples differ from catalog');
    process.stdout.write('README examples match catalog\n');
  } else {
    await writeFile(readmeUrl, replaceExamples(readme, rendered), 'utf8');
    process.stdout.write('README examples updated\n');
  }
}

if (process.argv[1] && fileURLToPath(import.meta.url) === resolve(process.argv[1])) {
  main().catch((error) => {
    process.stderr.write(error.message + '\n');
    process.exitCode = 1;
  });
}
