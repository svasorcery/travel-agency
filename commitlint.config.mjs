export default {
  extends: ['@commitlint/config-conventional'],
  rules: {
    'header-max-length':  [2, 'always', 100],
    'body-max-line-length': [1, 'always', 120],
    'scope-enum': [2, 'always', [
      // module scopes
      'flights', 'hotels', 'rail', 'trips', 'identity',
      // app scopes
      'host', 'ai', 'web', 'aspire',
      // shared scopes
      'shared', 'api-client', 'ui-kit',
      // cross-cutting
      'ci', 'tooling', 'docs', 'deps', 'arch', 'test', 'infra',
    ]],
  },
};
