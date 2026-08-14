import assert from 'node:assert/strict';
import { mkdir, mkdtemp, readFile, rm, symlink, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { validateDeliveryWorkflow, validateDotnetInventory } from './validate-dotnet-inventory.mjs';

const APP = 'apps/Sample.Api/Sample.Api.csproj';
const LIBRARY = 'modules/sample/Sample.Core/Sample.Core.csproj';
const TEST_PROJECT = 'tests/Sample.Tests/Sample.Tests.csproj';
const TOOL = 'tools/Sample.Tool/Sample.Tool.csproj';

function githubExpression(body) {
  return ['$', `{{ ${body} }}`].join('');
}

async function put(root, relativePath, content) {
  const path = join(root, ...relativePath.split('/'));
  await mkdir(dirname(path), { recursive: true });
  await writeFile(path, content, 'utf8');
}

function projectXml({ sdk = 'Microsoft.NET.Sdk', testProject = false } = {}) {
  return [
    `<Project Sdk="${sdk}">`,
    '  <PropertyGroup>',
    ...(testProject ? ['    <IsTestProject>true</IsTestProject>'] : []),
    '  </PropertyGroup>',
    ...(testProject
      ? [
          '  <ItemGroup>',
          '    <PackageReference Include="Microsoft.NET.Test.Sdk" />',
          '    <PackageReference Include="xunit.v3" />',
          '  </ItemGroup>',
        ]
      : []),
    '</Project>',
    '',
  ].join('\n');
}

function solutionXml(paths) {
  return `<Solution>\n${paths.map((path) => `  <Project Path="${path}" />`).join('\n')}\n</Solution>\n`;
}

function validManifest() {
  return {
    schemaVersion: 1,
    projects: [
      { path: APP, classification: 'application' },
      { path: LIBRARY, classification: 'library' },
      {
        path: TEST_PROJECT,
        classification: 'test',
        coverage: { mode: 'full', lane: 'sample-tests' },
      },
      { path: TOOL, classification: 'tool' },
    ],
    lanes: [{ id: 'sample-tests', job: 'test-sample', project: TEST_PROJECT }],
  };
}

const TRANSPORT_IMAGE_STEP = `      - name: Pre-pull transport images
        timeout-minutes: 5
        run: |
          docker pull pgvector/pgvector:pg17
          docker pull nats:2.12
`;

function completeDeliveryWorkflow() {
  return `name: CI

on:
  push:
    branches: [dev, master]
  pull_request:
    branches: [dev, master]
  workflow_dispatch:
    inputs:
      run_paid_ai_evals:
        type: boolean
        default: false

env:
  NX_BASE: ${githubExpression('github.event.pull_request.base.sha || github.event.before')}
  NX_HEAD: ${githubExpression('github.sha')}
  NX_CLOUD_ACCESS_TOKEN: ${githubExpression('secrets.NX_CLOUD_ACCESS_TOKEN')}

jobs:
  lint:
    steps:
      - uses: actions/checkout@11d5960a326750d5838078e36cf38b85af677262 # v4
      - run: npm run check:ai-harness
      - run: npm run check:dotnet-inventory
      - run: npx biome ci .
      - run: dotnet csharpier check .
  build-dotnet:
    steps:
      - run: dotnet restore Travel.slnx --no-cache
      - run: dotnet build Travel.slnx --no-restore
  frontend-affected:
    steps:
      - run: npx nx affected -t build test lint --exclude=travel-agency --base=$NX_BASE --head=$NX_HEAD
  test-sample:
    steps:
      - run: dotnet restore ${TEST_PROJECT} --no-cache
      - run: dotnet build ${TEST_PROJECT} --no-restore
      - run: dotnet test ${TEST_PROJECT} --no-build
  test-host-integration:
    steps:
      - uses: actions/setup-dotnet@67a3573c9a986a3f9c594539f4ab511d57bb3ce9
${TRANSPORT_IMAGE_STEP}      - run: dotnet restore tests/Travel.Host.Tests.Integration/Travel.Host.Tests.Integration.csproj --no-cache
  test-aspire-smoke:
    steps:
      - name: Pre-pull Aspire images
        timeout-minutes: 10
        run: |
          docker pull pgvector/pgvector:pg17
          docker pull redis:8.6
          docker pull nats:2.12
          docker pull quay.io/keycloak/keycloak:26.6
          docker pull dpage/pgadmin4:9.15.0
          docker pull axllent/mailpit:v1.20
  test-ai-evals:
    if: github.event_name == 'workflow_dispatch' && inputs.run_paid_ai_evals == true
    environment: paid-ai-evals
    steps:
      - uses: actions/checkout@11d5960a326750d5838078e36cf38b85af677262
      - uses: actions/setup-dotnet@67a3573c9a986a3f9c594539f4ab511d57bb3ce9
      - run: test -n "$ANTHROPIC_API_KEY"
        env:
          ANTHROPIC_API_KEY: ${githubExpression('secrets.ANTHROPIC_API_KEY')}
      - run: dotnet restore tests/Travel.Tests.AiEvals/Travel.Tests.AiEvals.csproj --no-cache
      - run: dotnet build tests/Travel.Tests.AiEvals/Travel.Tests.AiEvals.csproj --no-restore
      - run: dotnet test tests/Travel.Tests.AiEvals/Travel.Tests.AiEvals.csproj
        env:
          ANTHROPIC_API_KEY: ${githubExpression('secrets.ANTHROPIC_API_KEY')}
  test-e2e:
    needs:
      - lint
      - build-dotnet
      - frontend-affected
      - test-flights-unit
      - test-ai
      - test-host-http
      - test-architecture
      - test-contract
      - test-flights-integration
      - test-host-integration
      - test-aspire-smoke
    if: github.event_name == 'pull_request' && (github.base_ref == 'dev' || github.base_ref == 'master')
    steps:
      - name: Pre-pull Aspire images
        timeout-minutes: 10
        run: |
          docker pull pgvector/pgvector:pg17
          docker pull redis:8.6
          docker pull nats:2.12
          docker pull quay.io/keycloak/keycloak:26.6
          docker pull dpage/pgadmin4:9.15.0
          docker pull axllent/mailpit:v1.20
      - run: npx nx e2e travel-e2e
`;
}

async function writeManifest(root, manifest) {
  await put(root, 'tools/ci/dotnet-inventory.json', `${JSON.stringify(manifest, null, 2)}\n`);
}

async function createValidFixture(t) {
  const root = await mkdtemp(join(tmpdir(), 'travel-dotnet-inventory-'));
  t.after(() => rm(root, { recursive: true, force: true }));

  await put(root, APP, projectXml({ sdk: 'Microsoft.NET.Sdk.Web' }));
  await put(root, LIBRARY, projectXml());
  await put(root, TEST_PROJECT, projectXml({ testProject: true }));
  await put(root, 'tests/Sample.Tests/SampleTests.cs', 'public class SampleTests { [Fact] public void Works() { } }\n');
  await put(root, TOOL, projectXml({ sdk: 'Microsoft.NET.Sdk.Web' }));
  await put(root, 'Travel.slnx', solutionXml([APP, LIBRARY, TEST_PROJECT, TOOL]));
  await put(
    root,
    '.github/workflows/ci.yml',
    completeDeliveryWorkflow().replace(
      `      - test-flights-unit
      - test-ai
      - test-host-http
      - test-architecture
      - test-contract
      - test-flights-integration
      - test-host-integration
      - test-aspire-smoke
`,
      '      - test-sample\n',
    ),
  );
  const manifest = validManifest();
  await writeManifest(root, manifest);
  return { root, manifest };
}

function codes(issues) {
  return new Set(issues.map(({ code }) => code));
}

test('a complete inventory with one exact full-project CI lane is valid', async (t) => {
  const { root } = await createValidFixture(t);
  assert.deepEqual(await validateDotnetInventory(root), []);
});

test('the complete delivery workflow contract is valid', () => {
  assert.deepEqual(validateDeliveryWorkflow(completeDeliveryWorkflow()), []);
});

test('Docker-backed jobs pre-pull every Aspire image within a separate bounded step', () => {
  for (const workflow of [
    completeDeliveryWorkflow().replace('        timeout-minutes: 10\n', ''),
    completeDeliveryWorkflow().replace('          docker pull redis:8.6\n', ''),
    completeDeliveryWorkflow().replace('      - name: Pre-pull Aspire images\n', '      - name: Echo Aspire images\n'),
  ]) {
    assert.ok(codes(validateDeliveryWorkflow(workflow)).has('ci/aspire-images'));
  }
});

test('host integration pre-pulls its exact transport images in one unconditional bounded step', () => {
  const mutations = [
    ['missing', TRANSPORT_IMAGE_STEP, ''],
    ['renamed', 'Pre-pull transport images', 'Warm transport images'],
    [
      'wrong order',
      'docker pull pgvector/pgvector:pg17\n          docker pull nats:2.12',
      'docker pull nats:2.12\n          docker pull pgvector/pgvector:pg17',
    ],
    [
      'extra command',
      '          docker pull nats:2.12\n',
      '          docker pull nats:2.12\n          docker pull redis:8.6\n',
    ],
    [
      'conditional',
      '        timeout-minutes: 5\n        run: |',
      '        timeout-minutes: 5\n        if: success()\n        run: |',
    ],
    [
      'continue on error',
      '        timeout-minutes: 5\n        run: |',
      '        timeout-minutes: 5\n        continue-on-error: true\n        run: |',
    ],
    [
      'double-quoted condition',
      '        timeout-minutes: 5\n        run: |',
      '        timeout-minutes: 5\n        "if": false\n        run: |',
    ],
    [
      'single-quoted condition',
      '        timeout-minutes: 5\n        run: |',
      "        timeout-minutes: 5\n        'if': false\n        run: |",
    ],
    [
      'double-quoted continue on error',
      '        timeout-minutes: 5\n        run: |',
      '        timeout-minutes: 5\n        "continue-on-error": true\n        run: |',
    ],
    [
      'single-quoted continue on error',
      '        timeout-minutes: 5\n        run: |',
      "        timeout-minutes: 5\n        'continue-on-error': true\n        run: |",
    ],
    ['wrong timeout', '        timeout-minutes: 5\n        run: |', '        timeout-minutes: 6\n        run: |'],
    ['folded run block', '        run: |\n', '        run: >\n'],
    ['duplicate', TRANSPORT_IMAGE_STEP, `${TRANSPORT_IMAGE_STEP}${TRANSPORT_IMAGE_STEP}`],
    [
      'before setup',
      `      - uses: actions/setup-dotnet@67a3573c9a986a3f9c594539f4ab511d57bb3ce9\n${TRANSPORT_IMAGE_STEP}`,
      `${TRANSPORT_IMAGE_STEP}      - uses: actions/setup-dotnet@67a3573c9a986a3f9c594539f4ab511d57bb3ce9\n`,
    ],
    [
      'after restore',
      `${TRANSPORT_IMAGE_STEP}      - run: dotnet restore tests/Travel.Host.Tests.Integration/Travel.Host.Tests.Integration.csproj --no-cache\n`,
      `      - run: dotnet restore tests/Travel.Host.Tests.Integration/Travel.Host.Tests.Integration.csproj --no-cache\n${TRANSPORT_IMAGE_STEP}`,
    ],
  ];

  const acceptedMutations = [];
  for (const [name, target, replacement] of mutations) {
    const workflow = completeDeliveryWorkflow().replace(target, replacement);
    if (!codes(validateDeliveryWorkflow(workflow)).has('ci/transport-images')) acceptedMutations.push(name);
  }
  assert.deepEqual(acceptedMutations, []);
});

test('commented and echoed YAML fields cannot satisfy delivery commands or gates', () => {
  const workflow = completeDeliveryWorkflow()
    .replace(/^(\s*)- run: (.+)$/gm, '$1- run: echo "$2"')
    .replace(/^[ ]{4}if: /gm, '    # if: ')
    .replace(/^[ ]{4}environment: /gm, '    # environment: ');
  const found = codes(validateDeliveryWorkflow(workflow));
  for (const code of ['ci/lint', 'ci/build', 'ci/frontend-targets', 'ci/paid-evals', 'ci/e2e']) {
    assert.ok(found.has(code), `expected ${code}`);
  }
});

test('required delivery jobs and steps cannot be conditional or non-gating', () => {
  for (const workflow of [
    completeDeliveryWorkflow().replace('  lint:\n', '  lint:\n    continue-on-error: true\n'),
    completeDeliveryWorkflow().replace(
      '      - run: npm run check:ai-harness\n',
      '      - run: npm run check:ai-harness\n        if: false\n',
    ),
    completeDeliveryWorkflow().replace(
      '      - run: test -n "$ANTHROPIC_API_KEY"\n',
      '      - run: test -n "$ANTHROPIC_API_KEY"\n        continue-on-error: true\n',
    ),
    completeDeliveryWorkflow().replace('  test-e2e:\n', '  test-e2e:\n    continue-on-error: true\n'),
  ]) {
    assert.ok(codes(validateDeliveryWorkflow(workflow)).has('ci/non-gating'));
  }
});

test('required jobs reject custom shells that can bypass run steps', () => {
  for (const [name, workflow] of [
    [
      'step shell',
      completeDeliveryWorkflow().replace(
        '      - run: npm run check:ai-harness\n',
        '      - run: npm run check:ai-harness\n        shell: true {0}\n',
      ),
    ],
    [
      'workflow default shell',
      completeDeliveryWorkflow().replace('jobs:\n', 'defaults:\n  run:\n    shell: true {0}\n\njobs:\n'),
    ],
    [
      'job default shell',
      completeDeliveryWorkflow().replace('  lint:\n', '  lint:\n    defaults:\n      run:\n        shell: true {0}\n'),
    ],
    [
      'quoted-first step shell',
      completeDeliveryWorkflow().replace(
        '      - run: npm run check:ai-harness\n',
        "      - 'shell': true {0}\n        run: npm run check:ai-harness\n",
      ),
    ],
  ]) {
    assert.ok(codes(validateDeliveryWorkflow(workflow)).has('ci/shell'), name);
  }
});

test('named run steps and the repository-default fallback base are active workflow fields', () => {
  const workflow = completeDeliveryWorkflow()
    .replace(
      '      - run: npm run check:ai-harness',
      '      - name: Validate AI harness\n        run: npm run check:ai-harness',
    )
    .replace(
      githubExpression('github.event.pull_request.base.sha || github.event.before'),
      githubExpression(
        "github.event.pull_request.base.sha || github.event.before || format('origin/{0}', github.event.repository.default_branch)",
      ),
    );
  assert.deepEqual(validateDeliveryWorkflow(workflow), []);
});

test('echoed dotnet commands cannot satisfy a manifest lane', async (t) => {
  const { root } = await createValidFixture(t);
  const workflow = completeDeliveryWorkflow().replace(
    /^(\s*)- run: (dotnet (?:restore|build|test).+)$/gm,
    '$1- run: echo "$2"',
  );
  await put(root, '.github/workflows/ci.yml', workflow);
  const found = codes(await validateDotnetInventory(root));
  for (const code of ['lane/restore', 'lane/build', 'lane/project']) {
    assert.ok(found.has(code), `expected ${code}`);
  }
});

test('a dotnet lane that only lists tests does not satisfy execution coverage', async (t) => {
  const { root } = await createValidFixture(t);
  await put(
    root,
    '.github/workflows/ci.yml',
    completeDeliveryWorkflow().replace(
      `      - run: dotnet test ${TEST_PROJECT} --no-build\n`,
      `      - run: dotnet test ${TEST_PROJECT} --no-build --list-tests\n`,
    ),
  );
  assert.ok(codes(await validateDotnetInventory(root)).has('lane/execution'));
});

test('an unquoted background operator cannot make a required command non-gating', async (t) => {
  const { root } = await createValidFixture(t);
  await put(
    root,
    '.github/workflows/ci.yml',
    completeDeliveryWorkflow().replace(
      `      - run: dotnet test ${TEST_PROJECT} --no-build\n`,
      `      - run: dotnet test ${TEST_PROJECT} --no-build &\n`,
    ),
  );
  assert.ok(codes(await validateDotnetInventory(root)).has('lane/execution'));
});

test('quoted logger and filter separators remain valid gating test arguments', async (t) => {
  const { root } = await createValidFixture(t);
  await put(
    root,
    '.github/workflows/ci.yml',
    completeDeliveryWorkflow().replace(
      `      - run: dotnet test ${TEST_PROJECT} --no-build\n`,
      `      - run: dotnet test ${TEST_PROJECT} --no-build --filter "Category!=Integration&Category!=AspireSmoke" --logger "trx;LogFileName=sample.trx"\n`,
    ),
  );
  const issues = await validateDotnetInventory(root);
  assert.equal(
    issues.some(({ code }) => code === 'lane/execution'),
    false,
  );
});

test('a repository csproj absent from the inventory is rejected', async (t) => {
  const { root } = await createValidFixture(t);
  await put(root, 'modules/sample/Sample.Extra/Sample.Extra.csproj', projectXml());
  assert.ok(codes(await validateDotnetInventory(root)).has('inventory/missing'));
});

test('solution membership drift is rejected in either direction', async (t) => {
  const { root } = await createValidFixture(t);
  await put(root, 'Travel.slnx', solutionXml([APP, LIBRARY, TEST_PROJECT]));
  assert.ok(codes(await validateDotnetInventory(root)).has('solution/mismatch'));
});

test('commented solution projects do not count as active members', async (t) => {
  const { root } = await createValidFixture(t);
  await put(
    root,
    'Travel.slnx',
    solutionXml([APP, LIBRARY, TEST_PROJECT]).replace(
      '</Solution>',
      `  <!-- <Project Path="${TOOL}" /> -->\n</Solution>`,
    ),
  );
  assert.ok(codes(await validateDotnetInventory(root)).has('solution/mismatch'));
});

test('commented test metadata does not classify a project as a test', async (t) => {
  const { root } = await createValidFixture(t);
  await put(
    root,
    TEST_PROJECT,
    `<Project Sdk="Microsoft.NET.Sdk">
  <!-- <IsTestProject>true</IsTestProject> -->
  <!-- <PackageReference Include="Microsoft.NET.Test.Sdk" /> -->
  <!-- <PackageReference Include="xunit.v3" /> -->
</Project>
`,
  );
  assert.ok(codes(await validateDotnetInventory(root)).has('classification/mismatch'));
});

test('unrecognized solution Project element shapes fail closed', async (t) => {
  const { root } = await createValidFixture(t);
  const solution = await readFile(join(root, 'Travel.slnx'), 'utf8');
  await put(root, 'Travel.slnx', solution.replace('<Project Path=', '<Project Include='));
  assert.ok(codes(await validateDotnetInventory(root)).has('solution/schema'));
});

test('an executable tool cannot be mislabeled as a test project', async (t) => {
  const { root, manifest } = await createValidFixture(t);
  const tool = manifest.projects.find(({ path }) => path === TOOL);
  tool.classification = 'test';
  tool.coverage = { mode: 'full', lane: 'tool-tests' };
  manifest.lanes.push({ id: 'tool-tests', job: 'test-tool', project: TOOL });
  await put(
    root,
    '.github/workflows/ci.yml',
    `${completeDeliveryWorkflow()}\n  test-tool:\n    steps:\n      - run: dotnet restore ${TOOL}\n      - run: dotnet build ${TOOL}\n      - run: dotnet test ${TOOL}\n`,
  );
  await writeManifest(root, manifest);
  assert.ok(codes(await validateDotnetInventory(root)).has('classification/mismatch'));
});

test('a non-empty test project without an assigned lane is rejected', async (t) => {
  const { root, manifest } = await createValidFixture(t);
  delete manifest.projects.find(({ path }) => path === TEST_PROJECT).coverage;
  await writeManifest(root, manifest);
  assert.ok(codes(await validateDotnetInventory(root)).has('coverage/missing'));
});

test('an empty-scaffold declaration fails when a Fact or Theory appears', async (t) => {
  const { root, manifest } = await createValidFixture(t);
  const testProject = manifest.projects.find(({ path }) => path === TEST_PROJECT);
  testProject.coverage = { mode: 'empty-scaffold', reason: 'Reserved for the future module slice.' };
  manifest.lanes = [];
  await writeManifest(root, manifest);
  assert.ok(codes(await validateDotnetInventory(root)).has('coverage/empty'));
});

test('two unfiltered full-project lanes for one project are rejected as overlapping', async (t) => {
  const { root, manifest } = await createValidFixture(t);
  manifest.lanes.push({ id: 'sample-tests-copy', job: 'test-sample-copy', project: TEST_PROJECT });
  await put(
    root,
    '.github/workflows/ci.yml',
    `${completeDeliveryWorkflow()}\n  test-sample-copy:\n    steps:\n      - run: dotnet restore ${TEST_PROJECT}\n      - run: dotnet build ${TEST_PROJECT}\n      - run: dotnet test ${TEST_PROJECT}\n`,
  );
  await writeManifest(root, manifest);
  assert.ok(codes(await validateDotnetInventory(root)).has('coverage/overlap'));
});

test('an off-solution project requires excluded-legacy classification and a reason', async (t) => {
  const { root, manifest } = await createValidFixture(t);
  const legacy = 'src/Legacy/Legacy.csproj';
  await put(root, legacy, projectXml());
  manifest.projects.push({ path: legacy, classification: 'library' });
  await writeManifest(root, manifest);
  assert.ok(codes(await validateDotnetInventory(root)).has('legacy/exclusion'));

  manifest.projects.at(-1).classification = 'excluded-legacy';
  await writeManifest(root, manifest);
  assert.ok(codes(await validateDotnetInventory(root)).has('legacy/reason'));
});

test('a lane whose job is absent from CI is rejected', async (t) => {
  const { root, manifest } = await createValidFixture(t);
  manifest.lanes[0].job = 'missing-job';
  await writeManifest(root, manifest);
  assert.ok(codes(await validateDotnetInventory(root)).has('lane/job'));
});

test('a filtered lane whose exact filter is absent from its CI job is rejected', async (t) => {
  const { root, manifest } = await createValidFixture(t);
  manifest.projects.find(({ path }) => path === TEST_PROJECT).coverage = {
    mode: 'filtered',
    lanes: ['sample-tests'],
  };
  manifest.lanes[0].filter = 'Category=Unit';
  await writeManifest(root, manifest);
  assert.ok(codes(await validateDotnetInventory(root)).has('lane/filter'));
});

test('manifest paths must use exact repository casing and forward slashes', async (t) => {
  const { root, manifest } = await createValidFixture(t);
  manifest.projects[0].path = 'Apps\\Sample.Api\\Sample.Api.csproj';
  await writeManifest(root, manifest);
  const found = codes(await validateDotnetInventory(root));
  assert.ok(found.has('inventory/path'));
  assert.ok(found.has('inventory/missing'));
});

test('traversal, duplicate entries, and unknown schema keys fail closed', async (t) => {
  const { root, manifest } = await createValidFixture(t);
  manifest.extra = true;
  manifest.projects.push({ ...manifest.projects[0] });
  manifest.projects[1].path = '../outside.csproj';
  await writeManifest(root, manifest);
  const found = codes(await validateDotnetInventory(root));
  assert.ok(found.has('inventory/schema'));
  assert.ok(found.has('inventory/duplicate'));
  assert.ok(found.has('inventory/path'));
});

test('malformed project and lane entries report schema issues instead of throwing', async (t) => {
  const { root, manifest } = await createValidFixture(t);
  manifest.projects[0] = null;
  manifest.lanes[0] = null;
  await writeManifest(root, manifest);
  let issues;
  await assert.doesNotReject(async () => {
    issues = await validateDotnetInventory(root);
  });
  assert.ok(codes(issues).has('inventory/schema'));
  assert.ok(issues.some(({ path }) => path.endsWith('#projects[0]')));
  assert.ok(issues.some(({ path }) => path.endsWith('#lanes[0]')));
});

test('filtered lanes must assign each xUnit test to exactly one lane', async (t) => {
  const { root, manifest } = await createValidFixture(t);
  manifest.projects.find(({ path }) => path === TEST_PROJECT).coverage = {
    mode: 'filtered',
    lanes: ['unit-a', 'unit-b'],
  };
  manifest.lanes = [
    { id: 'unit-a', job: 'unit-a', project: TEST_PROJECT, filter: 'Category!=Integration' },
    { id: 'unit-b', job: 'unit-b', project: TEST_PROJECT, filter: 'Category!=AspireSmoke' },
  ];
  await put(
    root,
    '.github/workflows/ci.yml',
    `${completeDeliveryWorkflow()}\n  unit-a:\n    steps:\n      - run: dotnet restore ${TEST_PROJECT}\n      - run: dotnet build ${TEST_PROJECT}\n      - run: dotnet test ${TEST_PROJECT} --filter "Category!=Integration"\n  unit-b:\n    steps:\n      - run: dotnet restore ${TEST_PROJECT}\n      - run: dotnet build ${TEST_PROJECT}\n      - run: dotnet test ${TEST_PROJECT} --filter "Category!=AspireSmoke"\n`,
  );
  await writeManifest(root, manifest);
  assert.ok(codes(await validateDotnetInventory(root)).has('coverage/overlap'));
});

test('xUnit traits are associated with their class or method and comments are ignored', async (t) => {
  const { root, manifest } = await createValidFixture(t);
  manifest.projects.find(({ path }) => path === TEST_PROJECT).coverage = {
    mode: 'filtered',
    lanes: ['integration', 'aspire-smoke', 'http'],
  };
  manifest.lanes = [
    { id: 'integration', job: 'integration', project: TEST_PROJECT, filter: 'Category=Integration' },
    { id: 'aspire-smoke', job: 'aspire-smoke', project: TEST_PROJECT, filter: 'Category=AspireSmoke' },
    {
      id: 'http',
      job: 'http',
      project: TEST_PROJECT,
      filter: 'Category!=Integration&Category!=AspireSmoke',
    },
  ];
  await put(
    root,
    'tests/Sample.Tests/SampleTests.cs',
    `[Trait("Category", "Integration")]
public class IntegrationTests
{
    [Fact] public void Uses_class_trait() { }
}

public class HttpTests
{
    // [Trait("Category", "Integration")] is intentionally absent.
    [Fact] public void Is_trait_free() { }

    [Trait("Category", "AspireSmoke")]
    [Fact] public void Uses_method_trait() { }
}
`,
  );
  const laneJobs = manifest.lanes
    .map(
      ({ id, project, filter }) =>
        `  ${id}:\n    steps:\n      - run: dotnet restore ${project}\n      - run: dotnet build ${project}\n      - run: dotnet test ${project} --filter "${filter}"\n`,
    )
    .join('');
  await put(root, '.github/workflows/ci.yml', `${completeDeliveryWorkflow()}\n${laneJobs}`);
  await writeManifest(root, manifest);

  const issues = await validateDotnetInventory(root);
  assert.deepEqual(
    issues.filter(({ code }) => code === 'coverage/gap' || code === 'coverage/overlap'),
    [],
  );
});

test('multiline class and method traits participate in filtered-lane overlap detection', async (t) => {
  const { root, manifest } = await createValidFixture(t);
  manifest.projects.find(({ path }) => path === TEST_PROJECT).coverage = {
    mode: 'filtered',
    lanes: ['integration', 'aspire-smoke', 'http'],
  };
  manifest.lanes = [
    { id: 'integration', job: 'integration', project: TEST_PROJECT, filter: 'Category=Integration' },
    { id: 'aspire-smoke', job: 'aspire-smoke', project: TEST_PROJECT, filter: 'Category=AspireSmoke' },
    {
      id: 'http',
      job: 'http',
      project: TEST_PROJECT,
      filter: 'Category!=Integration&Category!=AspireSmoke',
    },
  ];
  await put(
    root,
    'tests/Sample.Tests/SampleTests.cs',
    `[
    Trait(
        "Category",
        "Integration"
    )
]
public class MixedTests
{
    [
        Trait(
            "Category",
            "AspireSmoke"
        )
    ]
    [Fact]
    public void Matches_two_lanes() { }
}
`,
  );
  const laneJobs = manifest.lanes
    .map(
      ({ id, project, filter }) =>
        `  ${id}:\n    steps:\n      - run: dotnet restore ${project}\n      - run: dotnet build ${project}\n      - run: dotnet test ${project} --filter "${filter}"\n`,
    )
    .join('');
  await put(root, '.github/workflows/ci.yml', `${completeDeliveryWorkflow()}\n${laneJobs}`);
  await writeManifest(root, manifest);
  assert.ok(codes(await validateDotnetInventory(root)).has('coverage/overlap'));
});

test('empty scaffolds detect multiline Fact and Theory attributes', async (t) => {
  const { root, manifest } = await createValidFixture(t);
  const testProject = manifest.projects.find(({ path }) => path === TEST_PROJECT);
  testProject.coverage = { mode: 'empty-scaffold', reason: 'Reserved for future tests.' };
  manifest.lanes = [];
  await put(
    root,
    'tests/Sample.Tests/SampleTests.cs',
    `public class SampleTests
{
    [
        Fact
    ]
    public void Fact_test() { }

    [
        Theory
    ]
    public void Theory_test() { }
}
`,
  );
  await writeManifest(root, manifest);
  assert.ok(codes(await validateDotnetInventory(root)).has('coverage/empty'));
});

test('inventory-owned project paths must not be symbolic links', async (t) => {
  const { root, manifest } = await createValidFixture(t);
  const target = join(root, ...LIBRARY.split('/'));
  const linkedPath = 'modules/sample/Sample.Linked/Sample.Linked.csproj';
  const link = join(root, ...linkedPath.split('/'));
  await mkdir(dirname(link), { recursive: true });
  try {
    await symlink(target, link, 'file');
  } catch (error) {
    if (error?.code === 'EPERM') {
      t.skip('creating symlinks requires Windows Developer Mode');
      return;
    }
    throw error;
  }
  const solution = await readFile(join(root, 'Travel.slnx'), 'utf8');
  await put(root, 'Travel.slnx', solution.replace('</Solution>', `  <Project Path="${linkedPath}" />\n</Solution>`));
  manifest.projects.push({ path: linkedPath, classification: 'library' });
  await writeManifest(root, manifest);
  assert.ok(codes(await validateDotnetInventory(root)).has('filesystem/symlink'));
});

test('push and pull request triggers must cover dev and master', () => {
  const workflow = completeDeliveryWorkflow().replace('branches: [dev, master]', 'branches: [master]');
  assert.ok(codes(validateDeliveryWorkflow(workflow)).has('ci/triggers'));
});

test('external GitHub Actions must use immutable full commit SHAs', () => {
  const workflow = completeDeliveryWorkflow().replace(
    'actions/checkout@11d5960a326750d5838078e36cf38b85af677262',
    'actions/checkout@v4',
  );
  assert.ok(codes(validateDeliveryWorkflow(workflow)).has('ci/action-pins'));
});

test('CI requires an explicit restore and build of Travel.slnx', () => {
  const workflow = completeDeliveryWorkflow().replace('      - run: dotnet build Travel.slnx --no-restore\n', '');
  assert.ok(codes(validateDeliveryWorkflow(workflow)).has('ci/build'));
});

test('frontend affected build test and lint use the actual pull request base SHA', () => {
  const workflow = completeDeliveryWorkflow()
    .replace('github.event.pull_request.base.sha || github.event.before', "'origin/master'")
    .replace('-t build test lint', '-t build test');
  const found = codes(validateDeliveryWorkflow(workflow));
  assert.ok(found.has('ci/frontend-base'));
  assert.ok(found.has('ci/frontend-targets'));
});

test('frontend affected excludes the non-frontend workspace-root project', () => {
  const workflow = completeDeliveryWorkflow().replace(' --exclude=travel-agency', '');
  assert.ok(codes(validateDeliveryWorkflow(workflow)).has('ci/frontend-scope'));
});

test('each dotnet test lane restores and builds its own exact project', async (t) => {
  const { root } = await createValidFixture(t);
  const workflow = completeDeliveryWorkflow().replace(`      - run: dotnet build ${TEST_PROJECT} --no-restore\n`, '');
  await put(root, '.github/workflows/ci.yml', workflow);
  assert.ok(codes(await validateDotnetInventory(root)).has('lane/build'));
});

test('filtered lanes require the exact effective filter instead of a narrowing substring', async (t) => {
  const { root, manifest } = await createValidFixture(t);
  const testProject = manifest.projects.find(({ path }) => path === TEST_PROJECT);
  testProject.coverage = { mode: 'filtered', lanes: ['sample-tests'] };
  manifest.lanes[0].filter = 'Category=Integration';
  await put(
    root,
    'tests/Sample.Tests/SampleTests.cs',
    '[Trait("Category", "Integration")] public class SampleTests { [Fact] public void Works() { } }\n',
  );
  await writeManifest(root, manifest);
  await put(
    root,
    '.github/workflows/ci.yml',
    completeDeliveryWorkflow().replace(
      `      - run: dotnet test ${TEST_PROJECT} --no-build\n`,
      `      - run: dotnet test ${TEST_PROJECT} --no-build --filter "Category=Integration&Category=DefinitelyAbsent"\n`,
    ),
  );

  assert.ok(codes(await validateDotnetInventory(root)).has('lane/filter'));
});

test('filtered lanes reject alternate selector surfaces beside the declared filter', async (t) => {
  for (const [name, extraSelector] of [
    ['MSBuild property', '-p:VSTestTestCaseFilter=Category=DefinitelyAbsent'],
    ['runsettings file', '--settings hidden.runsettings'],
    ['response file', '@hidden.rsp'],
  ]) {
    const { root, manifest } = await createValidFixture(t);
    const testProject = manifest.projects.find(({ path }) => path === TEST_PROJECT);
    testProject.coverage = { mode: 'filtered', lanes: ['sample-tests'] };
    manifest.lanes[0].filter = 'Category=Integration';
    await put(
      root,
      'tests/Sample.Tests/SampleTests.cs',
      '[Trait("Category", "Integration")] public class SampleTests { [Fact] public void Works() { } }\n',
    );
    await writeManifest(root, manifest);
    await put(
      root,
      '.github/workflows/ci.yml',
      completeDeliveryWorkflow().replace(
        `      - run: dotnet test ${TEST_PROJECT} --no-build\n`,
        `      - run: dotnet test ${TEST_PROJECT} --no-build --filter Category=Integration ${extraSelector}\n`,
      ),
    );

    assert.ok(codes(await validateDotnetInventory(root)).has('lane/filter'), name);
  }
});

test('filtered lanes reject shell escapes that change the effective filter', async (t) => {
  const { root, manifest } = await createValidFixture(t);
  const testProject = manifest.projects.find(({ path }) => path === TEST_PROJECT);
  testProject.coverage = { mode: 'filtered', lanes: ['sample-tests'] };
  manifest.lanes[0].filter = 'Category=Integration';
  await put(
    root,
    'tests/Sample.Tests/SampleTests.cs',
    '[Trait("Category", "Integration")] public class SampleTests { [Fact] public void Works() { } }\n',
  );
  await writeManifest(root, manifest);
  const workflow = completeDeliveryWorkflow()
    .replace(
      `      - test-flights-unit
      - test-ai
      - test-host-http
      - test-architecture
      - test-contract
      - test-flights-integration
      - test-host-integration
      - test-aspire-smoke
`,
      '      - test-sample\n',
    )
    .replace(
      `      - run: dotnet test ${TEST_PROJECT} --no-build\n`,
      `      - run: dotnet test ${TEST_PROJECT} --no-build --filter "Category\\=Integration"\n`,
    );
  await put(root, '.github/workflows/ci.yml', workflow);

  assert.ok(codes(await validateDotnetInventory(root)).has('lane/filter'));
});

test('manifest lane jobs cannot become conditional', async (t) => {
  const { root } = await createValidFixture(t);
  const workflow = completeDeliveryWorkflow()
    .replace('      - test-flights-unit\n', '      - test-sample\n      - test-flights-unit\n')
    .replace('  test-sample:\n', '  test-sample:\n    if: false\n');
  await put(root, '.github/workflows/ci.yml', workflow);

  assert.ok(codes(await validateDotnetInventory(root)).has('ci/non-gating'));
});

test('E2E must depend on every normal manifest lane job', async (t) => {
  const { root } = await createValidFixture(t);
  await put(root, '.github/workflows/ci.yml', completeDeliveryWorkflow());

  assert.ok(codes(await validateDotnetInventory(root)).has('ci/e2e-needs'));
});

test('paid AI evals require a boolean manual input and protected environment', () => {
  const workflow = completeDeliveryWorkflow()
    .replace('        type: boolean\n', '')
    .replace('    environment: paid-ai-evals\n', '');
  assert.ok(codes(validateDeliveryWorkflow(workflow)).has('ci/paid-evals'));
});

test('paid AI evals fail closed when the protected credential is unavailable', () => {
  for (const workflow of [
    completeDeliveryWorkflow().replace('      - run: test -n "$ANTHROPIC_API_KEY"\n', ''),
    completeDeliveryWorkflow().replace(
      new RegExp(`[ ]{8}env:\\n[ ]{10}ANTHROPIC_API_KEY: \\${githubExpression('secrets.ANTHROPIC_API_KEY')}\\n`),
      '',
    ),
    completeDeliveryWorkflow().replace(
      `      - run: dotnet test tests/Travel.Tests.AiEvals/Travel.Tests.AiEvals.csproj\n        env:\n          ANTHROPIC_API_KEY: ${githubExpression('secrets.ANTHROPIC_API_KEY')}\n`,
      '      - run: dotnet test tests/Travel.Tests.AiEvals/Travel.Tests.AiEvals.csproj\n',
    ),
    completeDeliveryWorkflow()
      .replace(
        new RegExp(`[ ]{8}env:\\n[ ]{10}ANTHROPIC_API_KEY: \\${githubExpression('secrets.ANTHROPIC_API_KEY')}\\n`, 'g'),
        '',
      )
      .replace(
        '    steps:\n',
        `    env:\n      ANTHROPIC_API_KEY: ${githubExpression('secrets.ANTHROPIC_API_KEY')}\n    steps:\n`,
      ),
  ]) {
    assert.ok(codes(validateDeliveryWorkflow(workflow)).has('ci/paid-evals'));
  }
});

test('paid AI evals forbid broader or additional credential scope', () => {
  const secret = githubExpression('secrets.ANTHROPIC_API_KEY');
  for (const [scope, workflow] of [
    ['workflow', completeDeliveryWorkflow().replace('env:\n', `env:\n  ANTHROPIC_API_KEY: ${secret}\n`)],
    [
      'job',
      completeDeliveryWorkflow().replace(
        '    environment: paid-ai-evals\n',
        `    environment: paid-ai-evals\n    env:\n      ANTHROPIC_API_KEY: ${secret}\n`,
      ),
    ],
    [
      'other-step',
      completeDeliveryWorkflow().replace(
        '      - run: dotnet test tests/Travel.Tests.AiEvals/Travel.Tests.AiEvals.csproj\n',
        `      - run: echo credential-consumer\n        env:\n          ANTHROPIC_API_KEY: ${secret}\n      - run: dotnet test tests/Travel.Tests.AiEvals/Travel.Tests.AiEvals.csproj\n`,
      ),
    ],
    [
      'bracket-reference-alias',
      completeDeliveryWorkflow().replace('env:\n', `env:\n  LEAKED_ANTHROPIC: \${{ secrets['ANTHROPIC_API_KEY'] }}\n`),
    ],
    [
      'whole-secrets-context',
      completeDeliveryWorkflow().replace('env:\n', `env:\n  LEAKED_ANTHROPIC: \${{ toJSON(secrets) }}\n`),
    ],
    ['inherited-secrets', completeDeliveryWorkflow().replace('  lint:\n', '  lint:\n    secrets: inherit\n')],
    [
      'commented-inherited-secrets',
      completeDeliveryWorkflow().replace('  lint:\n', '  lint:\n    secrets: inherit # bypass\n'),
    ],
    ['quoted-inherited-secrets', completeDeliveryWorkflow().replace('  lint:\n', "  lint:\n    secrets: 'inherit'\n")],
    [
      'unapproved-step-environment',
      completeDeliveryWorkflow().replace(
        `      - run: dotnet test ${TEST_PROJECT} --no-build\n`,
        `      - run: dotnet test ${TEST_PROJECT} --no-build\n        env:\n          VSTEST_TESTCASEFILTER: Category=DefinitelyAbsent\n`,
      ),
    ],
    [
      'quoted-step-environment',
      completeDeliveryWorkflow().replace(
        `      - run: dotnet test ${TEST_PROJECT} --no-build\n`,
        `      - run: dotnet test ${TEST_PROJECT} --no-build\n        "env":\n          VSTEST_TESTCASEFILTER: Category=DefinitelyAbsent\n`,
      ),
    ],
    [
      'mapping-first-step-environment',
      completeDeliveryWorkflow().replace(
        '      - run: npm run check:ai-harness\n',
        '      - env:\n          BASH_ENV: ./bypass.sh\n        run: npm run check:ai-harness\n',
      ),
    ],
    [
      'quoted-mapping-first-step-environment',
      completeDeliveryWorkflow().replace(
        '      - run: npm run check:ai-harness\n',
        "      - 'env':\n          BASH_ENV: ./bypass.sh\n        run: npm run check:ai-harness\n",
      ),
    ],
  ]) {
    const issueCodes = codes(validateDeliveryWorkflow(workflow));
    assert.ok(issueCodes.has('ci/paid-evals') || issueCodes.has('ci/environment'), scope);
  }
});

test('paid AI credential preflight runs before restore build and test', () => {
  const preflight = `      - run: test -n "$ANTHROPIC_API_KEY"\n        env:\n          ANTHROPIC_API_KEY: ${githubExpression('secrets.ANTHROPIC_API_KEY')}\n`;
  const paidTest = `      - run: dotnet test tests/Travel.Tests.AiEvals/Travel.Tests.AiEvals.csproj\n        env:\n          ANTHROPIC_API_KEY: ${githubExpression('secrets.ANTHROPIC_API_KEY')}\n`;
  const workflow = completeDeliveryWorkflow().replace(preflight, '').replace(paidTest, `${paidTest}${preflight}`);

  assert.ok(codes(validateDeliveryWorkflow(workflow)).has('ci/paid-evals'));
});

test('paid AI credential preflight precedes dotnet work hidden in a run block', () => {
  const workflow = completeDeliveryWorkflow().replace(
    '      - run: test -n "$ANTHROPIC_API_KEY"\n',
    '      - run: |\n          dotnet restore tests/Travel.Tests.AiEvals/Travel.Tests.AiEvals.csproj\n      - run: test -n "$ANTHROPIC_API_KEY"\n',
  );

  assert.ok(codes(validateDeliveryWorkflow(workflow)).has('ci/paid-evals'));
});

test('paid AI job rejects unapproved setup actions before credential preflight', () => {
  const workflow = completeDeliveryWorkflow().replace(
    '      - run: test -n "$ANTHROPIC_API_KEY"\n',
    '      - uses: ./.github/actions/restore-paid\n      - run: test -n "$ANTHROPIC_API_KEY"\n',
  );

  assert.ok(codes(validateDeliveryWorkflow(workflow)).has('ci/paid-evals'));
});

test('paid AI evals keep the credential scoped to the preflight and test steps', () => {
  assert.deepEqual(validateDeliveryWorkflow(completeDeliveryWorkflow()), []);
});

test('E2E runs only for pull requests targeting dev or master', () => {
  const workflow = completeDeliveryWorkflow().replace(" || github.base_ref == 'master'", '');
  assert.ok(codes(validateDeliveryWorkflow(workflow)).has('ci/e2e'));
});

test('E2E requires an active gating Nx target execution', () => {
  const workflow = completeDeliveryWorkflow().replace(
    '      - run: npx nx e2e travel-e2e\n',
    '      - run: echo "npx nx e2e travel-e2e"\n',
  );
  assert.ok(codes(validateDeliveryWorkflow(workflow)).has('ci/e2e'));
});

test('E2E depends on every normal required build and test lane', () => {
  const workflow = completeDeliveryWorkflow().replace('      - test-contract\n', '');
  assert.ok(codes(validateDeliveryWorkflow(workflow)).has('ci/e2e-needs'));
});

test('E2E rejects undeclared extra prerequisites that can skip the gate', () => {
  const workflow = completeDeliveryWorkflow()
    .replace('jobs:\n', 'jobs:\n  skip-e2e:\n    if: false\n    steps:\n      - run: true\n')
    .replace('      - lint\n', '      - lint\n      - skip-e2e\n');

  assert.ok(codes(validateDeliveryWorkflow(workflow)).has('ci/e2e-needs'));
});

test('E2E permits bounded block scripts and an always-running cleanup around its gating command', () => {
  const workflow = completeDeliveryWorkflow().replace(
    '      - run: npx nx e2e travel-e2e\n',
    `      - name: Start test service
        run: |
          start-service &
      - run: npx nx e2e travel-e2e
      - name: Cleanup
        if: always()
        run: |
          stop-service || true
`,
  );
  assert.deepEqual(validateDeliveryWorkflow(workflow), []);
});
