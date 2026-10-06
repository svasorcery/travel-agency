import assert from 'node:assert/strict';
import { mkdir, mkdtemp, readFile, realpath, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { test } from 'node:test';

async function load() {
  const m = await import('./snapshot.mjs').catch((e) => {
    if (e.code === 'ERR_MODULE_NOT_FOUND' && e.url === new URL('./snapshot.mjs', import.meta.url).href) return {};
    throw e;
  });
  assert.equal(typeof m.validateSnapshotPaths, 'function', 'working snapshot is not implemented');
  return m;
}
test('working snapshot accepts only explicit T1/T2 files and rejects secrets, traversal and foreign edits', async () => {
  const { validateSnapshotPaths } = await load();
  assert.deepEqual(
    validateSnapshotPaths([
      'tools/openspec/run.mjs',
      'package.json',
      '.agents/skills/openspec-explore/SKILL.md',
      'openspec/config.yaml',
    ]),
    ['.agents/skills/openspec-explore/SKILL.md', 'openspec/config.yaml', 'package.json', 'tools/openspec/run.mjs'],
  );
  for (const path of [
    '../private.env',
    'C:/private.env',
    '.env',
    'apps/Travel.Host/Program.cs',
    'tools/openspec/unknown.mjs',
    'tools/openspec/node_modules/foreign',
    '.agents/skills/openspec-foreign/SKILL.md',
    '.git/config',
  ])
    assert.throws(() => validateSnapshotPaths([path]), /snapshot|allowed|unsafe|scope/i);
});
test('snapshot overlay copies exact bytes without commits and detects later ignored writes', async () => {
  const { overlayWorkingSnapshot, workspaceDigest } = await load();
  const base = await realpath(await mkdtemp(join(tmpdir(), 'travel-snapshot-test-')));
  try {
    const source = join(base, 'source'),
      target = join(base, 'target');
    await mkdir(join(source, 'tools', 'openspec'), { recursive: true });
    await mkdir(target);
    await writeFile(join(source, 'tools', 'openspec', 'run.mjs'), 'owned code');
    const report = await overlayWorkingSnapshot(source, target, ['tools/openspec/run.mjs']);
    assert.equal(await readFile(join(target, 'tools', 'openspec', 'run.mjs'), 'utf8'), 'owned code');
    assert.match(report.digest, /^[a-f0-9]{64}$/);
    const before = await workspaceDigest(target);
    await writeFile(join(target, '.env'), 'unexpected');
    assert.notEqual(await workspaceDigest(target), before);
  } finally {
    await rm(base, { recursive: true, force: true });
  }
});
test('successful command execution proof requires the actual pinned launcher output, not final model prose', async () => {
  const { validatePinnedCommandProof } = await load();
  const root = join(tmpdir(), 'verified-root');
  const item = {
    type: 'item.completed',
    item: {
      type: 'command_execution',
      command: 'node tools/openspec/run.mjs status --change flights-m3-cancellation --json',
      status: 'completed',
      exit_code: 0,
      aggregated_output: JSON.stringify({
        changeName: 'flights-m3-cancellation',
        schemaName: 'spec-driven',
        changeRoot: join(root, 'openspec', 'changes', 'flights-m3-cancellation'),
      }),
    },
  };
  assert.equal(validatePinnedCommandProof([item], root).changeName, 'flights-m3-cancellation');
  assert.throws(
    () =>
      validatePinnedCommandProof(
        [{ type: 'item.completed', item: { type: 'agent_message', text: item.item.aggregated_output } }],
        root,
      ),
    /command|proof/i,
  );
  assert.throws(
    () => validatePinnedCommandProof([{ ...item, item: { ...item.item, command: 'openspec status --json' } }], root),
    /command|proof/i,
  );
  assert.throws(
    () => validatePinnedCommandProof([{ ...item, item: { ...item.item, exit_code: 1 } }], root),
    /command|proof/i,
  );
});

test('command proof rejects a foreign launcher or code that only prints a command-looking string', async () => {
  const { validatePinnedCommandProof } = await load();
  const root = join(tmpdir(), 'verified-root');
  const output = JSON.stringify({
    changeName: 'flights-m3-cancellation',
    schemaName: 'spec-driven',
    changeRoot: join(root, 'openspec', 'changes', 'flights-m3-cancellation'),
  });
  for (const command of [
    'node /foreign/tools/openspec/run.mjs status --change flights-m3-cancellation --json',
    'python -c "print(\'node tools/openspec/run.mjs status --change flights-m3-cancellation --json\')"',
  ])
    assert.throws(
      () =>
        validatePinnedCommandProof(
          [
            {
              type: 'item.completed',
              item: {
                type: 'command_execution',
                command,
                status: 'completed',
                exit_code: 0,
                aggregated_output: output,
              },
            },
          ],
          root,
        ),
      /command|proof/i,
    );
});

test('working snapshot notices newly created nested ignored directories', async () => {
  const { workspaceDigest } = await load();
  const root = await realpath(await mkdtemp(join(tmpdir(), 'travel-snapshot-ignored-')));
  try {
    await mkdir(join(root, 'modules', 'flights'), { recursive: true });
    await writeFile(join(root, 'AGENTS.md'), 'baseline');
    const before = await workspaceDigest(root);
    await mkdir(join(root, 'modules', 'flights', 'node_modules'), { recursive: true });
    await writeFile(join(root, 'modules', 'flights', 'node_modules', 'unexpected'), 'write');
    assert.notEqual(await workspaceDigest(root), before);
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test('failed command proof reports only bounded diagnostic facts, never command or output content', async () => {
  const { validatePinnedCommandProof } = await load();
  assert.throws(
    () =>
      validatePinnedCommandProof(
        [
          {
            type: 'item.completed',
            item: {
              type: 'command_execution',
              command: 'private-command-token',
              status: 'completed',
              exit_code: 0,
              aggregated_output: 'private-output-token',
            },
          },
        ],
        join(tmpdir(), 'verified-root'),
      ),
    (error) => {
      assert.match(error.message, /executions=1/);
      assert.match(error.message, /invocation=false/);
      assert.ok(!error.message.includes('private-command-token'));
      assert.ok(!error.message.includes('private-output-token'));
      return true;
    },
  );
});

test('native proof fails closed on un-attested shell wrappers that can forge pinned JSON', async () => {
  const { validatePinnedCommandProof } = await load();
  const root = join(tmpdir(), 'wrapper-proof');
  const output = JSON.stringify({
    changeName: 'flights-m3-cancellation',
    schemaName: 'spec-driven',
    changeRoot: join(root, 'openspec', 'changes', 'flights-m3-cancellation'),
  });
  for (const command of [
    'C:/foreign/pwsh.exe -Command "node tools/openspec/run.mjs status --change flights-m3-cancellation --json"',
    'bash -c "node tools/openspec/run.mjs status --change flights-m3-cancellation --json"',
  ]) {
    assert.throws(
      () =>
        validatePinnedCommandProof(
          [
            {
              type: 'item.completed',
              item: {
                type: 'command_execution',
                command,
                status: 'completed',
                exit_code: 0,
                aggregated_output: output,
              },
            },
          ],
          root,
        ),
      /proof/,
    );
  }
});

test('approved status plan binds exact interpreter, no-profile shell, command and cwd', async () => {
  const m = await load();
  assert.equal(typeof m.matchesPinnedStatusPlan, 'function');
  const root = resolve('plan-root');
  const plan = {
    root,
    node: resolve('trusted/node.exe'),
    shell: resolve('trusted/pwsh.exe'),
    command:
      '& "' +
      resolve('trusted/node.exe') +
      '" "' +
      join(root, 'tools', 'openspec', 'run.mjs') +
      '" status --change flights-m3-cancellation --json',
  };
  const wrapper = '"' + plan.shell + '" -NoProfile -Command \'' + plan.command + "'";
  assert.equal(m.matchesPinnedStatusPlan(wrapper, plan), true);
  for (const bad of [
    wrapper.replace(plan.shell, resolve('foreign/pwsh.exe')),
    wrapper.replace('-NoProfile ', ''),
    wrapper.replace(plan.node, resolve('foreign/node.exe')),
    wrapper + '; echo fake',
  ])
    assert.equal(m.matchesPinnedStatusPlan(bad, plan), false);
});

test('planned status proof rejects changed item/cwd/JSON or multiple completions', async () => {
  const m = await load(),
    root = resolve('plan-root'),
    node = resolve('trusted/node.exe'),
    shell = resolve('trusted/pwsh.exe');
  const plan = {
    root,
    node,
    shell,
    command:
      '& "' +
      node +
      '" "' +
      join(root, 'tools', 'openspec', 'run.mjs') +
      '" status --change flights-m3-cancellation --json',
  };
  const item = {
    id: 'one',
    type: 'commandExecution',
    cwd: root,
    command: '"' + shell + '" -NoProfile -Command \'' + plan.command + "'",
    status: 'completed',
    exitCode: 0,
    aggregatedOutput: JSON.stringify({
      changeName: 'flights-m3-cancellation',
      schemaName: 'spec-driven',
      changeRoot: join(root, 'openspec', 'changes', 'flights-m3-cancellation'),
    }),
  };
  const event = { method: 'item/completed', params: { threadId: 'owned', turnId: 'turn', item } },
    ctx = { plan, threadId: 'owned', turnId: 'turn', approvedItemId: 'one' };
  assert.equal(m.validatePlannedStatusProof([event], ctx).changeName, 'flights-m3-cancellation');
  for (const patch of [
    { id: 'foreign' },
    { cwd: join(root, 'foreign') },
    { command: plan.command },
    { aggregatedOutput: '{}' },
    { exitCode: 1 },
  ])
    assert.throws(() =>
      m.validatePlannedStatusProof([{ ...event, params: { ...event.params, item: { ...item, ...patch } } }], ctx),
    );
  assert.throws(() => m.validatePlannedStatusProof([event, event], ctx));
});

test('status plan recognizes escaped Windows display paths without allowing foreign executable', async () => {
  const m = await load(),
    root = resolve('plan-root'),
    node = resolve('trusted/node.exe'),
    shell = resolve('trusted/pwsh.exe'),
    plan = {
      root,
      node,
      shell,
      command:
        '& "' +
        node +
        '" "' +
        join(root, 'tools', 'openspec', 'run.mjs') +
        '" status --change flights-m3-cancellation --json',
    };
  const wrapper = '"' + shell + '" -NoProfile -Command \'' + plan.command + "'";
  if (process.platform === 'win32')
    assert.equal(m.matchesPinnedStatusPlan(wrapper.replaceAll('\\', '\\\\'), plan), true);
  assert.equal(m.matchesPinnedStatusPlan(wrapper.replace(shell, resolve('foreign/pwsh.exe')), plan), false);
});

test('status plan decodes canonical JSON command display and rejects foreign provenance', async () => {
  const m = await load();
  const root = resolve('plan-root');
  const node = resolve('trusted/node.exe');
  const shell = resolve('trusted/pwsh.exe');
  const plan = {
    root,
    node,
    shell,
    command:
      '& "' +
      node +
      '" "' +
      join(root, 'tools', 'openspec', 'run.mjs') +
      '" status --change flights-m3-cancellation --json',
  };
  const display = JSON.stringify(shell) + ' -NoProfile -Command ' + JSON.stringify(plan.command);
  assert.equal(m.matchesPinnedStatusPlan(display, plan), true);
  assert.equal(
    m.matchesPinnedStatusPlan(
      JSON.stringify(resolve('foreign/pwsh.exe')) + ' -NoProfile -Command ' + JSON.stringify(plan.command),
      plan,
    ),
    false,
  );
  assert.equal(
    m.matchesPinnedStatusPlan(JSON.stringify(shell) + ' -Command ' + JSON.stringify(plan.command), plan),
    false,
  );
  assert.equal(
    m.matchesPinnedStatusPlan(
      JSON.stringify(shell) + ' -NoProfile -Command ' + JSON.stringify(plan.command + '; whoami'),
      plan,
    ),
    false,
  );
  assert.equal(
    m.matchesPinnedStatusPlan(
      JSON.stringify(shell) +
        ' -NoProfile -Command ' +
        JSON.stringify(plan.command).replaceAll(' ', String.fromCharCode(92) + 'u0020'),
      plan,
    ),
    false,
  );
});

test('archived-pilot status fixture is created only in the separate verifier tree', async () => {
  const { preparePinnedStatusFixture, workspaceDigest } = await load();
  assert.equal(typeof preparePinnedStatusFixture, 'function');
  const base = await realpath(await mkdtemp(join(tmpdir(), 'travel-status-fixture-')));
  try {
    const source = join(base, 'source'),
      target = join(base, 'target');
    await mkdir(join(source, 'openspec', 'changes', 'archive', '2026-10-06-flights-m3-cancellation'), {
      recursive: true,
    });
    await mkdir(target);
    const before = await workspaceDigest(source);
    assert.equal(await preparePinnedStatusFixture(source, target), true);
    const proposal = await readFile(
      join(target, 'openspec', 'changes', 'flights-m3-cancellation', 'proposal.md'),
      'utf8',
    );
    assert.match(proposal, /verifier fixture/);
    assert.match(proposal, /not product-change status/);
    assert.equal(await workspaceDigest(source), before);
  } finally {
    await rm(base, { recursive: true });
  }
});
test('status fixture preserves every existing active-change byte', async () => {
  const { preparePinnedStatusFixture, workspaceDigest } = await load();
  assert.equal(typeof preparePinnedStatusFixture, 'function');
  const base = await realpath(await mkdtemp(join(tmpdir(), 'travel-status-existing-')));
  try {
    const source = join(base, 'source'),
      target = join(base, 'target');
    await mkdir(source);
    await mkdir(join(target, 'openspec', 'changes', 'flights-m3-cancellation'), { recursive: true });
    await writeFile(
      join(target, 'openspec', 'changes', 'flights-m3-cancellation', 'proposal.md'),
      'existing author bytes',
    );
    const before = await workspaceDigest(target);
    assert.equal(await preparePinnedStatusFixture(source, target), false);
    assert.equal(await workspaceDigest(target), before);
  } finally {
    await rm(base, { recursive: true });
  }
});
test('status fixture refuses equal or overlapping source and verifier trees before writing', async () => {
  const { preparePinnedStatusFixture, workspaceDigest } = await load();
  assert.equal(typeof preparePinnedStatusFixture, 'function');
  const base = await realpath(await mkdtemp(join(tmpdir(), 'travel-status-overlap-')));
  try {
    const child = join(base, 'nested');
    await mkdir(child);
    const before = await workspaceDigest(base);
    for (const [source, target] of [
      [base, base],
      [base, child],
      [child, base],
    ])
      await assert.rejects(preparePinnedStatusFixture(source, target), /separate|overlap/);
    assert.equal(await workspaceDigest(base), before);
  } finally {
    await rm(base, { recursive: true });
  }
});
test('status fixture rejects an existing non-directory instead of overwriting it', async () => {
  const { preparePinnedStatusFixture, workspaceDigest } = await load();
  assert.equal(typeof preparePinnedStatusFixture, 'function');
  const base = await realpath(await mkdtemp(join(tmpdir(), 'travel-status-invalid-')));
  try {
    const source = join(base, 'source'),
      target = join(base, 'target');
    await mkdir(source);
    await mkdir(join(target, 'openspec', 'changes'), { recursive: true });
    await writeFile(join(target, 'openspec', 'changes', 'flights-m3-cancellation'), 'foreign file');
    const before = await workspaceDigest(target);
    await assert.rejects(preparePinnedStatusFixture(source, target), /directory/);
    assert.equal(await workspaceDigest(target), before);
  } finally {
    await rm(base, { recursive: true });
  }
});
