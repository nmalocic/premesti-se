# Continuous integration (PS-002)

`.github/workflows/ci.yml` runs on **all pull requests**, including documentation-only
changes, and pushes to `main`. There are no path filters. It uses the unprivileged
`pull_request` event, read-only repository permissions, and no application secrets.
Superseded runs on the same ref are cancelled; cancelled or incomplete required
checks must not allow a merge.

## Stable required checks

| Workflow | Check name | Checks performed |
| --- | --- | --- |
| CI | `.NET CI` | Locked restore, full Release solution build, isolated API unit tests and nonzero TRX verification, `dotnet format`, publish/assembly validation and uploads |
| CI | `Web CI` | `npm ci`, non-watch unit tests and nonzero JUnit verification, ESLint, Prettier, TypeScript and Vite production build, output validation and uploads |

These job names are the exact required status-check contexts. Keep them stable;
renaming requires updating protection and confirming the observed check names.
No step uses `continue-on-error`. A failing test, formatting/lint violation, missing
build output, or unsuccessful build/upload fails its application job.

## Local verification

Use .NET SDK **8.0.422**, enforced by `global.json`, and Node **22.23.0**, as used by
`deploy/Api.Dockerfile`. All projects target `net8.0` via `Directory.Build.props`.
Commit updated NuGet `packages.lock.json` files and `web/package-lock.json` whenever
dependencies change. For intentional .NET package changes, run unlocked
`dotnet restore PremestiSe.sln`, then verify locked restore again.

From the repository root, without Compose, VeloxDB, SMTP, or a running API:

```sh
dotnet restore PremestiSe.sln --locked-mode
dotnet build PremestiSe.sln -c Release --no-restore
dotnet test tests/PremestiSe.Api.UnitTests/PremestiSe.Api.UnitTests.csproj -c Release --no-build --no-restore --logger "trx;LogFileName=unit.trx" --results-directory TestResults/unit
python3 .github/scripts/verify-test-results.py TestResults/unit/unit.trx
dotnet format PremestiSe.sln --verify-no-changes --no-restore
bash .github/scripts/collect-dotnet-artifacts.sh
```

If only Docker is installed, run those .NET commands in
`mcr.microsoft.com/dotnet/sdk:8.0.422` with the repository mounted at `/workspace`,
working directory `/workspace`, and a persistent NuGet cache volume.

For the web application:

```sh
cd web
npm ci
npm run test:unit
python3 ../.github/scripts/verify-test-results.py test-results/unit/junit.xml
npm run lint
npm run format:check
npm run build
test -s dist/index.html
compgen -G 'dist/assets/*.js' > /dev/null
compgen -G 'dist/assets/*.css' > /dev/null
```

The output-validation snippets and collection script use Bash. Python 3 verifies
the report using only its standard library. To apply mechanical fixes, use
`dotnet format PremestiSe.sln --no-restore` and, from `web/`, `npm run format`.
ESLint checks TypeScript, React hooks, and Fast Refresh exports; generated output
is ignored by both ESLint and Prettier.

### Unit tests versus real-stack tests

`PremestiSe.Api.UnitTests` exercises existing `DatabaseHealthCheck` behavior through
an internal injectable operation: successful completion, synchronous/asynchronous
sanitized failures, bounded waiting, and cancellation. Production still constructs
the VeloxDB connection and applies the configured open/retry and HTTP wait budgets.
The explicit unit-project command avoids the intentionally empty domain suite and
the real-stack integration suite. TRX verification rejects zero executed tests,
skipped-only reports, failures, and unsuccessful outcomes.

Vitest discovers only `web/src/**/*.test.{ts,tsx}` and fails on empty discovery.
React Testing Library tests health rendering plus HTTP/database/network failure and
retry recovery using mocked fetch responses in jsdom. JUnit verification also
rejects skipped-only or failing suites. Playwright's `web/e2e/` is outside discovery.

`tests/PremestiSe.Api.Tests`, Playwright, and `bash deploy/verify.sh` remain real-stack
checks. Their service requirements and healthy/outage/recovery commands are
preserved in [README.md](../README.md#build-and-verify); they are not the isolated
CI command. Domain behavior and broader integration/E2E coverage belong to later
stories.

## Downloadable artifacts

All artifacts have **14-day retention**. Successful application jobs publish:

| Artifact | Contents |
| --- | --- |
| `api-release` | Explicit Release publish directory `artifacts/api/`, including API DLL, dependencies, runtime configuration and appsettings |
| `importer-release` | Explicit Release publish directory `artifacts/importer/`, including importer DLL, dependencies and runtime configuration |
| `database-assemblies` | `PremestiSe.Db.Model.dll`, `PremestiSe.Db.Api.dll`, `PremestiSe.Db.Contracts.dll`, `PremestiSe.Domain.dll` |
| `web-dist` | `web/dist/index.html` and Vite production JS/CSS assets |

The API and SPA are separate build artifacts; the API artifact is not a combined
deployment image. Database collection follows `deploy/VeloxDB.Dockerfile`'s build
output pattern, explicitly requiring every deployment assembly to exist and be
nonempty. API/importer manifests require their DLL, deps and runtimeconfig files;
web validation requires the HTML and generated JS/CSS. Build uploads fail on a
missing directory as well. Artifact publication does not deploy anything.

`dotnet-unit-results` contains TRX and `web-unit-results` contains JUnit XML.
Their upload steps run with `always()` so available results survive test failures.
If tests never start there may be no report to upload; this does not turn a failed
or cancelled job into success.

## Required merge protection (administrator action)

An administrator must configure and **read back** active enforcement on `main`:

1. Under **Settings → Rules → Rulesets**, create or update an **active** branch
   ruleset targeting the default branch. Enable **Require a pull request before
   merging**, **Require status checks to pass**, and **Require branches to be up
   to date before merging**.
2. Require the observed GitHub Actions contexts **`.NET CI`** and **`Web CI`**.
   Publish the workflow and run it first if GitHub needs checks to appear in the
   selector. Do not configure a bypass list or allow failures to be bypassed.
   Preserve existing review and other repository protections.
3. Alternatively, use branch protection for `main`, requiring those same contexts,
   strict/up-to-date status checks, PRs, and enforcement for administrators with
   no actors allowed to bypass pull requests.
4. Read back the ruleset/branch protection and compare names to an actual run:

   ```sh
   gh api repos/nmalocic/premesti-se/rulesets
   gh api repos/nmalocic/premesti-se/rulesets/RULESET_ID
   gh api repos/nmalocic/premesti-se/branches/main/protection
   gh run view RUN_ID --repo nmalocic/premesti-se --json jobs
   ```

   Confirm active enforcement targets `main`, both checks are required, strict
   status checks are enabled, and bypasses are absent. For classic protection,
   confirm `enforce_admins.enabled` is true too. A 403 is **not** proof protection
   is absent. An empty ruleset list says nothing about classic branch protection.
5. On a disposable PR, introduce a representative failing unit assertion, verify
   the actual required job fails, and confirm the PR cannot be merged normally
   in the GitHub UI/API. Record evidence without merging the failing PR or using
   a bypass. Remove the deliberate failure and confirm both checks recover.

### Hosted acceptance evidence

After an authorized contributor publishes the branch and the automation opens the
PR, verify the implementation's real runs, including a documentation-only PR:

```sh
gh run list --repo nmalocic/premesti-se --workflow ci.yml --limit 10
gh run view RUN_ID --repo nmalocic/premesti-se
gh run view RUN_ID --repo nmalocic/premesti-se --log-failed
gh run download RUN_ID --repo nmalocic/premesti-se --dir /tmp/ci-artifacts
```

Inspect all six artifact contents on a successful run. Also download available
test results from a failing-test run. In disposable changes, verify .NET and web
representative test failures, empty discovery, .NET formatting, ESLint and
Prettier violations, and missing required outputs fail their jobs. Restore every
deliberate failure. Verify a push to `main` runs both jobs when this is merged.
Local command success and workflow inspection do not replace this hosted proof.

### Implementation access status

On 2026-11-03 repository metadata reported `admin: false` and `push: false`, but
Git transport successfully published the implementation branch `ps-002-ci-pipeline`.
Reading `main` protection still returned HTTP 403 and the visible ruleset list was
empty. Hosted execution awaits the automation-created PR; branch pushes other than
`main` do not trigger this workflow. An administrator must apply/read back merge
protection. **PS-002 is not fully accepted until hosted runs/artifact
downloads and administrator read-back plus failing-PR merge refusal are recorded.**
The full merge-blocking requirement in `docs/user-stories.md` remains unchanged.
