# NX Cloud Free Tier (Hobby Plan)

## Status

**Manual activation required.** The interactive browser flow (`npx nx connect`) cannot be automated. A human must run this once.

## One-time setup steps

1. Run `npx nx connect` from the workspace root.
2. Sign in to [nx.app](https://nx.app) with GitHub.
3. Select the `travel-agency` workspace.
4. NX Cloud will write `nxCloudId` into `nx.json` — commit that change:

```bash
git add nx.json
git commit -m "ci(nx-cloud): connect NX Cloud Hobby plan for distributed cache + DTE"
```

5. Get the `NX_CLOUD_ACCESS_TOKEN` from workspace settings at nx.app.
6. Add it to GitHub repo secrets: **Settings → Secrets and variables → Actions → New repository secret**
   - Name: `NX_CLOUD_ACCESS_TOKEN`
   - Value: the token from step 5

The `NX_CLOUD_ACCESS_TOKEN` env reference is already declared in `.github/workflows/ci.yml` — no further workflow edit needed.

## Verification

After activation, run:

```bash
npx nx affected -t build --base=HEAD~1
```

Check the nx.app dashboard — task records should appear, confirming the Hobby tier is active.

## What you get (Hobby tier)

- Distributed remote build cache shared across all contributors and CI
- Distributed Task Execution (DTE) for parallel CI pipelines
- Affected command computes impact across branches

## Security note

Do NOT commit any access tokens to the repository. NX Cloud tokens belong in `.env.local` (gitignored) or CI secrets.
