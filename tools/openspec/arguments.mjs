import { READ_COMMANDS } from './policy.mjs';

const namePattern = /^[a-z0-9]+(?:-[a-z0-9]+)*(?:\/[a-z0-9]+(?:-[a-z0-9]+)*)*$/;
const grammars = {
  status: {
    pos: 0,
    flags: { '--change': 'id', '--json': 'bool', '--all': 'bool', '--schema': 'schema', '--no-color': 'bool' },
  },
  instructions: { pos: 1, flags: { '--change': 'id', '--json': 'bool', '--schema': 'schema', '--no-color': 'bool' } },
  context: { pos: 0, flags: { '--json': 'bool', '--no-color': 'bool' } },
  list: { pos: 0, flags: { '--json': 'bool', '--specs': 'bool', '--no-color': 'bool' } },
  show: {
    pos: 1,
    flags: {
      '--json': 'bool',
      '--type': 'type',
      '--no-scenarios': 'bool',
      '--deltas-only': 'bool',
      '--no-color': 'bool',
    },
  },
  validate: {
    pos: 1,
    optional: true,
    flags: {
      '--json': 'bool',
      '--strict': 'bool',
      '--all': 'bool',
      '--changes': 'bool',
      '--specs': 'bool',
      '--no-color': 'bool',
    },
  },
  schemas: { pos: 0, flags: { '--json': 'bool', '--no-color': 'bool' } },
};
export function validateCommandPrefix(args) {
  if (!Array.isArray(args) || !args.length || args.some((a) => typeof a !== 'string' || !a || a.includes('\0')))
    throw new Error('CLI arguments must be nonempty strings');
  if (args[0].startsWith('-')) {
    if (!['--version', '-V', '--help', '-h'].includes(args[0]) || args.length !== 1)
      throw new Error('Global flags must be isolated; a named command is required');
  }
  if (
    args.some(
      (a) => a === '--store' || a === '--store-path' || a.startsWith('--store=') || a.startsWith('--store-path='),
    )
  )
    throw new Error('Only the local Travel root is supported; store flags are forbidden');
}
export function validateReadArguments(args) {
  validateCommandPrefix(args);
  const command = args[0];
  if (command.startsWith('-')) return;
  const grammar = grammars[command];
  if (!grammar || !READ_COMMANDS.includes(command))
    throw new Error('Read-only scope refuses mutating or unsupported commands');
  const seen = new Set();
  let positional = 0;
  for (let i = 1; i < args.length; i++) {
    const arg = args[i];
    if (!arg.startsWith('-')) {
      if (!namePattern.test(arg) || ++positional > grammar.pos)
        throw new Error('Unsupported read-only positional arguments');
      continue;
    }
    const eq = arg.indexOf('=');
    const flag = eq < 0 ? arg : arg.slice(0, eq);
    const kind = grammar.flags[flag];
    if (!kind || seen.has(flag)) throw new Error('Unsupported or duplicate read-only flag');
    seen.add(flag);
    if (kind === 'bool') {
      if (eq >= 0) throw new Error('Unsupported boolean flag arguments');
      continue;
    }
    const value = eq >= 0 ? arg.slice(eq + 1) : args[++i];
    if (
      !value ||
      (kind === 'id' && !namePattern.test(value)) ||
      (kind === 'schema' && value !== 'spec-driven') ||
      (kind === 'type' && !['change', 'spec'].includes(value))
    )
      throw new Error('Unsupported read-only flag value');
  }
  if (!grammar.optional && positional !== grammar.pos) throw new Error('Missing read-only positional arguments');
}
export function validateForwardArguments(args, { readOnly = false } = {}) {
  validateCommandPrefix(args);
  if (READ_COMMANDS.includes(args[0])) {
    validateReadArguments(args);
    return;
  }
  if (readOnly) throw new Error('Read-only scope refuses mutating commands');
  if (
    args[0] === 'new' &&
    args.length >= 3 &&
    args[1] === 'change' &&
    namePattern.test(args[2]) &&
    (!args.slice(3).length || (args.length === 5 && args[3] === '--schema' && args[4] === 'spec-driven'))
  )
    return;
  if (
    args[0] === 'archive' &&
    args.length >= 2 &&
    namePattern.test(args[1]) &&
    args.slice(2).every((a) => a === '--yes') &&
    args.filter((a) => a === '--yes').length <= 1
  )
    return;
  throw new Error('Unsupported command/arguments: setup must use the staged launcher');
}
