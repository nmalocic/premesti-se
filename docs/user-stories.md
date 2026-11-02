# Premesti Se — Backlog (Epics, User Stories, Tasks)

Companion to [`architecture.md`](architecture.md). Each story has an ID, priority (**P0** = MVP blocker, **P1** = MVP, **P2** = post-MVP), release (**v1** / **v2**), a rough size (S/M/L), and acceptance criteria (AC) in Given/When/Then form where useful.

Personas:
- **Parent**: wants to move their child to another kindergarten.
- **Matched parent**: a participant in a found swap.
- **Operator**: the team running the platform.

---

## EPIC 0: Foundations & DevOps

### PS-001 · Solution scaffolding · P0 · v1 · M
*As a developer, I want a working skeleton of the solution so that the team can build features in parallel.*
- Create the .NET solution with the projects `Api`, `Domain`, `Db.Model`, `Db.Api`, `Db.Contracts`, `Importer`, and test projects. Create the React (Vite + TS) app in `/web`.
- Add `docker-compose` with the API, a VeloxDB server, and Mailpit (dev SMTP).
- **AC:** `docker compose up` starts everything. `GET /api/health` returns 200 including the DB check. The SPA loads and calls the API.

### PS-002 · CI pipeline · P0 · v1 · M
*As a developer, I want CI on every PR so that regressions are caught early.*
- **AC:** the pipeline builds .NET and web, runs unit tests, lint/format checks (dotnet format, ESLint, Prettier), and publishes artifacts. Merges are blocked on failure.

### PS-003 · CD to staging & prod · P1 · v1 · L
- **AC:** deploying to staging is automatic on main. Production deploys on a tag or manual approval. VeloxDB model/API assemblies are deployed as part of the pipeline. Secrets come from a vault or the environment.

### PS-004 · Observability baseline · P1 · v1 · M
- **AC:** Serilog structured logs, OpenTelemetry traces and metrics, and a dashboard showing request rate, errors, outbox backlog, and match count. An alert fires when the outbox backlog stays above 50 for more than 10 minutes.

### PS-005 · VeloxDB persistence & backup · P0 · v1 · M
*As an operator, I want durable storage and backups so that no requests are lost.*
- **AC:** persistence log and snapshot directories sit on persistent volumes. A nightly backup runs. A restore procedure is documented and tested once. A standby/replica is configured for prod.

---

## EPIC 1: Kindergarten registry (data import)

### PS-010 · Import kindergartens from xlsx · P0 · v1 · L
*As an operator, I want to import the official kindergarten list so that parents can pick their current and desired kindergarten.*
- Source: the master sheet `obdanista_csv_cyrillic` only.
- **AC:**
  - Given the xlsx, when the importer runs, then Regions (`uprava`), Cities, Municipalities (`opstina`), Institutions (`ustanova`), and Kindergartens are upserted.
  - Exact duplicate rows are collapsed. The current file has 3,602 rows → **1,802 unique**.
  - **Kosovo is excluded:** rows whose `uprava` is `Ранилуг` or `Косовска Митровица` (53 unique locations) are skipped, using the config list `Import:ExcludedRegions`. Expected result: **1,748 kindergartens**. Previously imported records from excluded regions are deactivated.
  - **Belgrade is grouped:** all 15 `Београд - *` municipalities map to one City, "Београд", and each kindergarten keeps its Municipality. The grouping comes from the config `Import:CityGroups`.
  - Numeric fields are normalized (`83.0` → `83`, `11460.0` → `11460`).
  - Malformed rows (for example a header merged into data, or `tip_lokacije` containing `управа`) are rejected and listed in an import report.
  - Re-running the importer is idempotent, because a stable `ExternalKey` hash means nothing is duplicated.
  - Kindergartens missing from a newer file are marked `IsActive = false`, not deleted.

### PS-011 · Latin transliteration for search · P1 · v1 · S
*As a parent who types in Latin script, I want to find "Novi Sad" and "Poletarac" so that I don't have to switch keyboards.*
- **AC:** every Region, City, Institution, and Kindergarten name has a generated `NameLat`. Search matches both scripts and ignores diacritics (`c`/`č`/`ć`).

### PS-012 · Geocode kindergartens · P0 · v1 · L
*As a parent, I want kindergartens shown at their real location on the map so that I can judge distance.*
- **AC:**
  - Each kindergarten gets `Lat`/`Lng` and `GeoPrecision` (`address` | `street` | `settlement`).
  - Rows with `/` or `бб` fall back to geocoding the settlement, plus the street when present.
  - Results are cached in a versioned file, so re-imports don't call the geocoder again.
  - The geocoder rate limits are respected (Nominatim allows 1 request/s).
  - A report lists low-precision results for manual review.

### PS-013 · Manual coordinate override · P2 · v1 · S
- **AC:** an operator can set coordinates for a kindergarten through a CSV or the admin CLI. Overrides survive re-imports.

---

## EPIC 2: Submit request

### PS-020 · Choose city · P0 · v1 · S
*As a parent, I want to pick my city first so that I only see relevant kindergartens.*
- **AC:**
  - A searchable dropdown of cities (municipalities). Belgrade appears once, as "Београд / Beograd".
  - Picking a city loads its kindergartens and centres the map on it.
  - For Belgrade, the kindergarten picker shows the municipality next to each name (for example "Пчелица — Земун") and offers an optional municipality filter.
  - Kosovo municipalities are not listed.

### PS-021 · Choose current and desired kindergarten · P0 · v1 · M
*As a parent, I want to select my child's current kindergarten and the one I want so that the system can find a swap.*
- **AC:**
  - Both pickers are searchable and show the name, institution, and address.
  - The current and desired kindergarten cannot be the same (validation error).
  - Both must belong to the selected city (while `SameCityOnly` is on). In Belgrade, they can be in different municipalities.

### PS-022 · Enter contact details · P0 · v1 · S
*As a parent, I want to enter my name, email, and phone so that matched parents can contact me.*
- **AC:** all fields are required. Email has RFC-valid syntax and is normalized to lower case. The phone is validated and stored in E.164 format (default `+381`). Client and server run the same validation and return field-level errors.

### PS-023 · Child age group · P0 · v1 · S
*As a parent, I want to state my child's age group so that I'm only matched with compatible swaps.*
- **AC:**
  - A required select (jaslice, mlađa, srednja, starija, predškolska), with a short hint about the age range of each group.
  - The value is stored on the request and can be edited while the request is `Active` (PS-061). Editing it triggers re-matching.
  - The matcher **never** combines requests from different age groups. This is a hard rule, not a config option.

### PS-024 · Consent & privacy notice · P0 · v1 · S
*As a parent, I want to understand how my data is used so that I can give informed consent.*
- **AC:** the consent checkbox is required. It says that contact details are shared **only with matched participants**, and links to the privacy policy. The consent timestamp and policy version are stored.

### PS-025 · Submit creates a Pending Request · P0 · v1 · M
*As a parent, I want my submission saved until I confirm my email so that fake emails can't create requests.*
- **AC:**
  - When the form is valid and the CAPTCHA passes, a `PendingRequest` is created with a hashed 256-bit token that expires after 24h, and a confirmation email is enqueued.
  - The response is 202, and the UI shows "Check your email" with a resend option.
  - If the email already has an active request, then no new pending request is created, and the parent gets an email with their manage link.

### PS-026 · Anti-abuse on submit · P0 · v1 · M
- **AC:** CAPTCHA (Turnstile/hCaptcha) is verified server-side. Rate limits are 5 submits per IP per hour and 3 per email per day. Going over the limit returns 429 with a friendly message.

### PS-027 · Multiple destination kindergartens · P1 · **v2** · L
*As a parent, I want to list several acceptable kindergartens in order of preference so that I have a better chance of a match.*
- **AC:** up to N destinations (config, default 5), ordered by priority. Each becomes a graph edge. When the matcher has several cycles of equal length, it prefers the higher-priority destination. When a request is matched, all of its edges are reserved.

---

## EPIC 3: Email verification

### PS-030 · Confirmation email with magic link · P0 · v1 · M
*As a parent, I want to confirm my request with one click so that it becomes active.*
- **AC:**
  - The email (sr-Cyrl/sr-Latn and plain text) has a link to `/confirm?token=…` and a 6-digit fallback code.
  - The landing page sends a **POST** to confirm, so link pre-fetchers can't activate it.
  - A valid, unexpired token moves the PendingRequest to a `Request[Active]`, deletes the pending row, and triggers matching.
  - Expired or used tokens show a clear message with a "resend" button.

### PS-031 · Resend confirmation · P1 · v1 · S
- **AC:** resending invalidates the previous token. It is limited to 3 per hour per email.

### PS-032 · Clean up unconfirmed requests · P1 · v1 · S
- **AC:** a job runs hourly and deletes PendingRequests older than 24h, including their personal data.

### PS-033 · Phone verification (SMS OTP) · P2 · **v2** · M
*As the platform, I want verified phone numbers so that matched parents can trust each other's contact details.*
- **AC:** an SMS with a 6-digit code (valid 10 minutes, 5 attempts). The request shows a "phone verified" badge. Matching can require verified phones (config).

### PS-034 · Additional KYC options · P2 · **v2** · L
- Spike: evaluate eUprava eID / ConsentID, or proof of enrolment (for example an uploaded PU confirmation), against the privacy cost.
- **AC:** a decision record with a recommended approach.

---

## EPIC 4: Matching engine

### PS-040 · Direct swap matching (2-cycle) · P0 · v1 · M
*As a parent, I want to be matched automatically with a parent who wants the opposite move so that we can swap.*
- **AC:**
  - Given an active request A (X→Y) and an active request B (Y→X) in the same city and age group, when either is confirmed, then a `Match[Proposed]` with A and B is created within 5 s.
  - Given the same requests but **different age groups**, no match is created.
  - Given kindergartens X in Земун and Y in Нови Београд, a swap is found, because Belgrade is one city.
  - A request is never in more than one open match.
  - Two requests from the same email are never matched with each other.
  - When several candidates exist, the oldest one wins (FIFO).

### PS-041 · Chain matching (N-cycle, configurable) · P1 · v1 · L
*As a parent, I want three or more families to be able to swap in a chain so that I can move even without a direct counterpart.*
- **AC:**
  - `Matching:MaxCycleLength` (2–6) is read from config.
  - Given A: X→Y, B: Y→Z, C: Z→X, all in the same age group, and max length ≥ 3, then one match of size 3 is created. If any one of them is in a different age group, no match is created.
  - Shorter cycles are preferred over longer ones.
  - Cycle search is bounded and partitioned by city. It finishes in under 1 s for 10k active requests (benchmark test).

### PS-042 · Atomic match reservation · P0 · v1 · M
- **AC:** finding and reserving a cycle happens in one VeloxDB transaction. A concurrency test shows that confirming several requests in parallel never puts a request in two matches.

### PS-043 · Periodic matching sweep · P1 · v1 · S
- **AC:** a job runs hourly or on demand and re-evaluates all active unmatched requests. It picks up requests released after rejections, expiries, or config changes.

### PS-044 · Matching domain library with tests · P0 · v1 · M
- **AC:** the algorithm lives in `PremestiSe.Domain`, which has no dependency on the DB. It has unit tests for the 2-, 3-, and 4-cycle, no-match, self-match exclusion, FIFO, age-group isolation, and Belgrade cross-municipality cases, plus property-based tests showing every produced cycle is valid and has disjoint participants.

---

## EPIC 5: Match lifecycle & notifications

### PS-050 · Match proposal email · P0 · v1 · M
*As a matched parent, I want to be told that a swap is possible so that I can act.*
- **AC:** each participant gets an email saying a swap was found, including the chain description (by kindergarten names only, no personal data yet), Accept/Decline links, and a deadline (72h by default).

### PS-051 · Accept / decline a match (everyone must accept) · P0 · v1 · M
*As a matched parent, I want a match to become final only when every participant has accepted, so that nobody is committed and no contact details are shared until everyone agrees.*
- **AC:**
  - A participant-scoped token opens the match page, which offers Accept or Decline. It shows how many participants have accepted ("2 of 3 accepted"), but not who they are.
  - While at least one participant hasn't responded, the match stays `Proposed` and no contact details are shared with anyone.
  - Only when **all** participants accept does the match become `Accepted` and trigger PS-052.
  - When anyone declines, the match becomes `Rejected`, the other requests go back to `Active` and are re-matched, and the decliner's request goes back to `Active` (or is withdrawn, if they choose).
  - When the deadline passes, the match becomes `Expired`. Non-responders' requests are paused and they get an email to reactivate. Everyone else goes back to `Active`.

### PS-052 · Contact details exchange email · P0 · v1 · S
*As a matched parent, I want the other participants' contact details and next steps so that we can go to the officials together.*
- **AC:** sent only when the match is `Accepted`. It contains every participant's name, email, and phone, who moves where, and a short guide to the official transfer procedure at the PU.

### PS-053 · Mark match completed · P1 · v1 · S
- **AC:** any participant can mark "Transfer done". This sets the match to `Completed` and its requests to `Fulfilled`. A match auto-closes 30 days after `Accepted` if nobody reports. The match is anonymised according to the retention policy.

### PS-054 · Transactional email outbox · P0 · v1 · M
- **AC:** every email is written to `EmailOutbox` in the same transaction as the domain change. The worker sends with exponential backoff (max 8 attempts) and marks the row sent or failed. A failed email raises an alert.

### PS-055 · Email templates & localization · P1 · v1 · M
- **AC:** templates for confirm, already-registered, match proposal, match accepted, match rejected or expired, expiry reminder, and withdrawal confirmation. Each exists in sr-Cyrl, sr-Latn, and en, with HTML and text versions. The sending domain is set up with SPF/DKIM/DMARC.

---

## EPIC 6: Manage my request

### PS-060 · Manage link & status page · P1 · v1 · M
*As a parent, I want to see the status of my request so that I know whether there's a match.*
- **AC:** the confirmation email and the success page include a manage link. The page shows the request details, the status (Active / Matched / …), and the open match, if any.

### PS-061 · Edit request · P1 · v1 · S
- **AC:** the parent can change the destination(s), phone, or age group while the request is `Active`. Editing triggers re-matching. Changing the email is not allowed (withdraw and re-submit instead).

### PS-062 · Withdraw & delete my data · P0 · v1 · S
*As a parent, I want to withdraw my request and delete my data so that I control my information.*
- **AC:** withdrawing sets the request to `Withdrawn` and removes the parent from any open match, which is dissolved and its other participants go back to `Active` with an email. Personal data is erased or anonymised immediately (or within 30 days, by policy). A confirmation email is sent.

### PS-063 · Request expiry & renewal · P1 · v1 · S
- **AC:** active requests expire after 90 days (config). A reminder email goes out 7 days before with a one-click "keep active". Expired requests are excluded from matching and the map.

---

## EPIC 7: Interactive map

### PS-070 · Aggregated map API · P0 · v1 · M
- **AC:** `GET /api/cities/{id}/map?ageGroup=` (the age-group filter is optional) returns, per kindergarten, the id, name, lat/lng, number of active outgoing requests, number of incoming requests, and the top 3 destinations and sources. It contains **no personal data**, is cached for 1–5 minutes, and responds in under 300 ms at p95.

### PS-071 · City map view · P0 · v1 · L
*As a parent, I want to see on a map where other parents want to move so that I can spot possible swaps.*
- **AC:**
  - When a city is selected, a Leaflet map (OSM tiles) centres on it and shows markers for its kindergartens, clustered when zoomed out.
  - Marker size or colour shows demand (outgoing vs incoming).
  - An age-group filter shows only the demand relevant to the parent's group. When opened from the manage page, it defaults to the parent's group.
  - Clicking a marker shows "N want to leave / M want to join", plus the top destinations and sources.
  - The map is usable on mobile, and a list view is available as the accessible alternative.

### PS-072 · Flow lines between kindergartens · P2 · v1 · M
- **AC:** a toggle draws arrows between kindergartens for active demand, with thickness proportional to the count. Only kindergartens with at least 1 request are shown.

### PS-073 · Start a request from the map · P1 · v1 · S
- **AC:** the popup has "I attend this kindergarten" and "I want this kindergarten" buttons. Each opens the form with the field pre-filled.

### PS-074 · Highlight my opportunity · P2 · v2 · M
*As a parent with an active request, I want the map to highlight kindergartens that would complete a chain for me.*
- **AC:** the manage page map highlights kindergartens where one more request would close a cycle that includes the parent.

---

## EPIC 8: Frontend shell, i18n, accessibility

### PS-080 · App shell & routing · P0 · v1 · M
- **AC:** routes for Home (map and CTA), New request, Confirm, Manage, Match, Privacy, and FAQ. Responsive and mobile-first. Errors from the API's `ProblemDetails` are shown in a consistent way.

### PS-081 · Localization (sr-Cyrl / sr-Latn / en) · P1 · v1 · M
- **AC:** a language switcher that remembers the choice. Every UI string is in i18n files. Kindergarten names are shown in the selected script.

### PS-082 · Accessibility (WCAG 2.1 AA) · P1 · v1 · M
- **AC:** keyboard navigation works, form labels and errors are announced, colour contrast meets AA, the map has a list alternative, and an axe check runs in CI.

### PS-083 · Landing page, FAQ & how-it-works · P1 · v1 · S
- **AC:** explains how the swap works, that the platform doesn't perform the transfer, the official steps at the PU, and privacy.

---

## EPIC 9: Security, privacy & compliance

### PS-090 · Privacy policy & data retention jobs · P0 · v1 · M
- **AC:** a privacy policy page (ZZPL-compliant, reviewed by legal). Scheduled jobs apply retention: pending requests after 24h, withdrawn or expired requests anonymised after 30 days, completed matches anonymised after 90 days.

### PS-091 · Token service · P0 · v1 · M
- **AC:** separate token types (confirm, manage, match-participant), each with its own expiry. Tokens are stored as SHA-256 hashes and compared in constant time. Manage and match tokens can be revoked.

### PS-092 · Security hardening · P1 · v1 · M
- **AC:** HTTPS with HSTS, CSP, CORS restricted to the SPA origin, and security headers. Dependency scanning runs in CI. An OWASP ZAP baseline scan runs on staging.

### PS-093 · Audit log · P1 · v1 · S
- **AC:** request confirmed, match created, decision, withdrawal, and erasure events are logged with the time, the actor (by token type), and the entity, with no unnecessary personal data.

---

## EPIC 10: Operations / admin

### PS-100 · Admin CLI · P1 · v1 · M
*As an operator, I want basic admin commands so that I can maintain data and handle abuse.*
- **AC:** commands to re-import kindergartens, deactivate a kindergarten, look up a request by email, remove a request (abuse), re-run matching, and show stats (requests per city, matches, conversion).

### PS-101 · Operator dashboard (read-only) · P2 · v2 · M
- **AC:** a web page behind SSO showing KPIs: active requests, matches proposed, accepted, and completed, median time to match, and email bounce rate.

---

## Spikes / decisions (resolve before or early in Sprint 1)

| ID | Spike | Output |
|---|---|---|
| PS-S1 | VeloxDB proof of concept: model, DbAPI operation, hosting in Docker, backup and restore | A working sample and an ADR |
| PS-S2 | Geocoding provider comparison (Nominatim vs paid) on 100 sample addresses | Accuracy report and a choice |
| PS-S3 | Remaining product decisions Q2, Q4, Q7–Q10 from `architecture.md` §11. Q1, Q3, Q5, and Q6 are already decided | Signed-off answers |
| PS-S5 | Check whether the missing Belgrade municipalities (Grocka, Savski Venac) are a gap in the source data | A data fix or confirmation |
| PS-S4 | Email provider choice and domain setup | Provider and DNS records |

---

## Suggested release plan

| Sprint | Focus | Stories |
|---|---|---|
| 0 | Spikes and foundations | PS-S1–S5, PS-001, PS-002, PS-005 |
| 1 | Data and form | PS-010, PS-011, PS-020–PS-024, PS-080, PS-091 |
| 2 | Verification and direct matching | PS-025, PS-026, PS-030, PS-031, PS-032, PS-040, PS-042, PS-044, PS-054 |
| 3 | Match lifecycle | PS-050–PS-053, PS-055, PS-060–PS-062 |
| 4 | Map | PS-012, PS-070, PS-071, PS-073 |
| 5 | Chains, hardening, launch | PS-041, PS-043, PS-063, PS-081–PS-083, PS-090, PS-092, PS-093, PS-100, PS-003, PS-004 |
| v2 | Extensions | PS-027, PS-033, PS-034, PS-074, PS-101, PS-013, PS-072 |
