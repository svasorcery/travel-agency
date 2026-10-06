export const PIN = Object.freeze({
  name: '@fission-ai/openspec',
  version: '1.14.0',
  integrity: 'sha512-V+zitRK918I6B3EIcYL3gVYoPoPgzknSxE1zL0zUQdbd4NsO5kbrxU5jVceuNNEznD94pbRPCvpCDIa6BeEaPA==',
  sourceCommit: '94ca9c1eb15d1b49c06c988419b75c3d95f8b2b5',
});
export const SKILLS = Object.freeze([
  'openspec-apply-change',
  'openspec-archive-change',
  'openspec-explore',
  'openspec-propose',
  'openspec-sync-specs',
  'openspec-update-change',
]);
export const WORKFLOWS = Object.freeze(['propose', 'explore', 'apply', 'update', 'sync', 'archive']);
export const CLI_COMMANDS = Object.freeze([
  'archive',
  'completion',
  'config',
  'context',
  'doctor',
  'feedback',
  'init',
  'instructions',
  'list',
  'new',
  'schema',
  'schemas',
  'show',
  'status',
  'store',
  'update',
  'validate',
  'view',
  'workset',
]);
export const READ_COMMANDS = Object.freeze([
  '--version',
  '-V',
  '--help',
  '-h',
  'status',
  'instructions',
  'list',
  'show',
  'validate',
  'context',
  'schemas',
]);
export const ADAPTER_VERSION = 1;
export const LAUNCHER = 'node tools/openspec/run.mjs';
