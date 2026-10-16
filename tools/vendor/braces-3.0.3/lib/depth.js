'use strict';

// Local security backport for GHSA-vfj7-8cjw-p6xm. This is a structural limit,
// independent of caller options and of the parser's character limit.
const MAX_AST_DEPTH = 128;

const assertDepth = depth => {
  if (depth > MAX_AST_DEPTH) {
    const error = new SyntaxError(`Brace structure exceeds maximum depth (${MAX_AST_DEPTH})`);
    error.code = 'ERR_BRACES_MAX_DEPTH';
    throw error;
  }
};

const assertAstDepth = ast => {
  const pending = [{ node: ast, depth: 0 }];
  while (pending.length > 0) {
    const { node, depth } = pending.pop();
    assertDepth(depth);
    if (node && node.nodes) {
      for (const child of node.nodes) pending.push({ node: child, depth: depth + 1 });
    }
  }
};

module.exports = { MAX_AST_DEPTH, assertDepth, assertAstDepth };
