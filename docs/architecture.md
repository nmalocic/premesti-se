# Premesti Se — Architecture Document

| | |
|---|---|
| Status | Draft v0.2: product decisions Q1, Q3, Q5, Q6 applied |
| Date | 2026-11-02 |
| Sources | `docs/breife.md`, `docs/obdanista_csv_cyrillic.xlsx` |

---

## 1. Purpose and scope

Premesti Se ("switch over") lets parents of preschool children find other parents who want to **swap kindergartens**. If parent A's child attends kindergarten X and they want Y, and parent B's child attends Y and they want X, both can apply to the officials for a mutual transfer. The platform:

1. collects a transfer request (current kindergarten → desired kindergarten) and verifies the parent's email,
2. finds matches automatically: direct 2-way swaps and, optionally, longer **swap chains** (A→B→C→A),
3. tells every participant in a match by email so they can exchange contact details and go to the officials,
4. shows open requests on an interactive city map, so parents can see where demand is.

The platform does **not** carry out the transfer. That happens offline, at the preschool institution (predškolska ustanova, "PU").

### 1.1 Release scope

| Capability | v1 (MVP) | v2 |
|---|---|---|
| Request form (name, email, phone, city, 1 source, 1 destination) | ✅ | |
| Email confirmation (magic link / OTP) and Pending → Active flow | ✅ | |
| Direct 2-way matching | ✅ | |
| Chain matching (N-way cycles, configurable max length) | ✅ (behind a config flag) | |
| Match email notifications | ✅ | |
| Interactive map of open requests by city | ✅ | |
| Several destination kindergartens per request | | ✅ |
| Additional identity checks (KYC: SMS OTP, eID) | | ✅ |

---

## 2. What the input data tells us

### 2.1 Brief (`breife.md`)
- **Stack:** .NET Core backend, **VeloxDB** database, **React** frontend.
- **Entities named:** Pending Request, Request, Match (implied), City, Kindergarten.
- **Matching:** direct swap plus a "graph" mode that combines several requests into a cycle, with the size set in configuration.

### 2.2 Kindergarten registry (`obdanista_csv_cyrillic.xlsx`)

There is one master sheet (`obdanista_csv_cyrillic`) and 17 per-region sheets (Београд, Нови Сад, Ниш …). Columns:

| Column | Meaning | Example | Notes |
|---|---|---|---|
| `uprava` | School administration region (Školska uprava) | Београд | 17 distinct values. **This is not a city.** |
| `opstina` | Municipality | Београд - Барајево | 169 distinct. Becomes the "City", except that all `Београд - *` municipalities are grouped into one city |
| `naselje` | Settlement / neighbourhood | Барајево, Београд (Земун) | 1,029 distinct |
| `ustanova` | Preschool institution (legal entity) | ПУ Полетарац | ~156 distinct (per region) |
| `naziv` | Facility / building name | Објекат Свети Сава | **The unit a child actually attends** |
| `ulica`, `broj` | Street, house number | Светосавска, 83.0 | `бб` = no number |
| `postanski_broj` | Postal code | 11460.0 | |
| `tip_lokacije` | Main or branch location | Матична / Издвојена локација | |

**Data quality problems the importer must handle:**

| # | Problem | Impact | Handling |
|---|---|---|---|
| D1 | The master sheet has **3,602 rows but only 1,802 unique ones**. Every row appears exactly twice | Duplicate kindergartens in the picker | De-duplicate on a normalized natural key |
| D2 | Numbers are stored as floats (`83.0`, `11460.0`) | Bad addresses | Convert to string; drop a trailing `.0` |
| D3 | One malformed row: the header got merged into the data (`Издвојена локација﻿управа`, …) and `broj = 43655.0` looks like an Excel date serial | Bad record | Reject the row and fix it by hand |
| D4 | Placeholder values `/` and `бб` in street or number (290 and 1,510 rows) | Geocoding fails or is imprecise | Fall back to geocoding by settlement |
| D5 | **No latitude/longitude** | The map can't be built without them | Geocode once at import time and store the coordinates (see §6.4) |
| D6 | The per-region sheets total 4,596 rows, which doesn't add up to the master sheet | Unclear which sheet is the source of truth | Use the **master sheet** as the only import source |
| D7 | All text is Serbian Cyrillic | Users may search in Latin script | Store Cyrillic and generate a Latin transliteration for search |
| D8 | Regions `Ранилуг` (15 locations) and `Косовска Митровица` (38 locations) are Kosovo administrations | Out of scope | **Decided:** the importer skips these regions through a configurable exclusion list (`Import:ExcludedRegions`). That leaves 1,748 kindergartens after de-duplication and dropping the malformed row |
| D9 | Belgrade appears as 15 separate municipalities (`Београд - Земун`, `Београд - Нови Београд`, …). Grocka and Savski Venac are missing from the source | Parents in Belgrade often move between municipalities | **Decided:** group them into a single city, "Београд" (see §3.2). Check whether the missing municipalities are a gap in the source data |

---

## 3. Domain model

### 3.1 Concepts

```
Region (uprava) 1─* Municipality/City (opstina) 1─* Kindergarten (naziv @ address)
                                                     *─1 Institution (ustanova)

Parent 1─* Request ──source──> Kindergarten
                   ──destination(s)──> Kindergarten     (v1: exactly 1, v2: 1..N ranked)

Match 1─* MatchParticipant *─1 Request
```

### 3.2 Entities

| Entity | Key fields | Notes |
|---|---|---|
| **Region** | Id, NameCyr, NameLat | From `uprava` |
| **City** | Id, RegionId, NameCyr, NameLat, CenterLat, CenterLng | From `opstina`, except that every `Београд - *` municipality maps to the single city **Београд**. Selected first in the form. Grouping rules live in importer config (`Import:CityGroups`) so other cities can be grouped later |
| **Municipality** | Id, CityId, NameCyr, NameLat | The original `opstina`. Kept for search, filtering, and map display (for example "Београд › Земун") |
| **Institution** | Id, Name, RegionId | From `ustanova` |
| **Kindergarten** | Id, CityId, MunicipalityId, InstitutionId, Name, Settlement, Street, Number, PostalCode, LocationType, Lat, Lng, GeoPrecision, IsActive, ExternalKey | `ExternalKey` = hash of the normalized row, so re-imports are idempotent |
| **Parent** | Id, FirstName, LastName, Email (unique, normalized), Phone (E.164), EmailVerifiedAt, CreatedAt | Personal data (see §8) |
| **PendingRequest** | Id, Parent snapshot, CityId, SourceKgId, DestinationKgIds, ChildAgeGroup*, TokenHash, ExpiresAt, CreatedAt | Deleted after confirmation or expiry |
| **Request** | Id, ParentId, CityId, SourceKgId, Destinations[] (KgId, Priority), ChildAgeGroup*, Status, CreatedAt, ConfirmedAt, ExpiresAt | Status: `Active`, `Matched`, `Withdrawn`, `Expired`, `Fulfilled` |
| **Match** | Id, Type (`Direct`/`Chain`), Size, Status, CreatedAt, ExpiresAt | Status: `Proposed`, `Accepted`, `Rejected`, `Expired`, `Completed` |
| **MatchParticipant** | MatchId, RequestId, From KgId, To KgId, Decision, DecidedAt | One row per edge in the cycle |
| **EmailOutbox** | Id, Template, To, Payload, Status, Attempts, NextAttemptAt | Transactional outbox (§6.3) |
| **AuditLog** | Id, Actor, Action, EntityRef, At | Security and GDPR evidence |

\* `ChildAgeGroup` is **required** (decided, Q1). A swap is only possible between children in the **same age group**: jaslice, mlađa, srednja, starija, or predškolska. It is set on the request (one request is one child) and is a hard matching rule, not a config option.

### 3.3 Request lifecycle

```
            submit                 confirm link
 (form) ───────────► PendingRequest ───────────► Request[Active]
                         │ 24h, not confirmed           │
                         ▼                              │ matcher finds a cycle
                     (deleted)                          ▼
                                               Request[Matched] ◄──── Match[Proposed]
                                                │        ▲               │ all accept → Accepted
              withdraw / expire (e.g. 90 days)  │        │ any rejects   │ → Completed (manual / timeout)
                                                ▼        └───────────────┘
                                   Withdrawn / Expired          (others go back to Active)
```

---

## 4. Matching engine

### 4.1 Model as a graph

- **Nodes** = kindergartens.
- **Edges** = active requests: a directed edge `source → destination` (v2: one edge per acceptable destination).
- The graph is **partitioned by (City, ChildAgeGroup)**. Edges in different age groups never connect, and Belgrade is one partition per age group because its municipalities are grouped.
- A **match** is a **directed cycle** in which every edge belongs to a different request (and a different parent).
  - Cycle length 2 = direct swap (A: X→Y, B: Y→X).
  - Cycle length k > 2 = chain (A: X→Y, B: Y→Z, C: Z→X).
- Configuration: `Matching:MaxCycleLength` (default **2** at launch, raise to 3–4 once the base works), `Matching:Enabled`, `Matching:SameCityOnly` (default true). The same-age-group rule is **always on** and cannot be configured.

### 4.2 Algorithm (incremental)

The matcher runs when a request becomes **Active**, starting from its new edge `s → d`:

1. Run a **bounded DFS/BFS** from `d` over edges of *Active, unmatched* requests in the same partition (city + age group), looking for a path back to `s` with length ≤ `MaxCycleLength − 1`.
2. Rank the candidate cycles: **shortest first** (fewer people to coordinate), then **oldest requests first** (FIFO fairness), then the parents' destination priority (v2).
3. Reserve the best cycle **in one transaction**: create the Match, set its requests to `Matched`, enqueue the emails. If a concurrent transaction has already taken one of those requests, retry with the next candidate.
4. A **periodic sweep** (for example hourly) re-runs the search over all Active requests. It catches cycles freed up by rejections or expiries, and changes to the configured cycle length.

Complexity: the graph is partitioned by city, and with k ≤ 4 the search is tiny (a city has at most a few hundred kindergartens). In-memory traversal in VeloxDB is cheap.

### 4.3 Accepting a match (decided)

A match becomes final only when **every participant accepts it**. The matcher creates a `Match[Proposed]` and each participant gets a link to **accept or decline**. Contact details are revealed only when the match becomes `Accepted`, after the last participant accepts. If anyone declines, or the 72h deadline passes, the match is dissolved, the other participants' requests go back to `Active`, and they are matched again. Repeated declines can be rate-limited.

---

## 5. System architecture

### 5.1 Context

```
                 ┌────────────────┐
  Parent ───────►│ React SPA      │──── map tiles ────► OpenStreetMap / MapTiler
  (browser)      └──────┬─────────┘
                        │ HTTPS / JSON (REST)
                 ┌──────▼─────────┐        ┌──────────────────────┐
                 │ ASP.NET Core   │◄──────►│ VeloxDB server       │
                 │ API (+ Worker) │  VeloxDB│ (in-memory, ACID,    │
                 └──────┬─────────┘ protocol│  persisted log)      │
                        │                   └──────────────────────┘
                        ▼
                 Email provider (SMTP / SendGrid / Mailgun / AWS SES)
                 CAPTCHA (hCaptcha / Cloudflare Turnstile)
```

### 5.2 Containers and components

| Container | Technology | Responsibilities |
|---|---|---|
| **Web (SPA)** | React 18 + TypeScript, Vite, React Router, TanStack Query, React Hook Form + Zod, Leaflet (`react-leaflet`), i18next (sr-Cyrl, sr-Latn, en) | Request form, confirmation landing page, match accept/reject pages, map, manage/withdraw a request |
| **API** | ASP.NET Core (.NET 8 LTS / .NET 10), Minimal APIs or Controllers, FluentValidation, rate limiting middleware, OpenAPI | Public REST endpoints, input validation, CAPTCHA check, token issuance and verification |
| **Worker** | .NET `BackgroundService` (same deployable at first, can be split later) | Email outbox dispatcher, expiry jobs, periodic matching sweep |
| **Database** | **VeloxDB** | System of record. Domain operations run as VeloxDB *database operations*: matching runs inside the DB transaction, which gives strong consistency for cycle reservation |
| **Importer** | .NET console tool | Parse xlsx → normalize → de-duplicate → geocode → upsert Kindergartens |
| **Admin (later)** | Simple React route behind auth, or CLI | Re-import data, deactivate a kindergarten, view stats, remove abusive requests |

### 5.3 VeloxDB notes

VeloxDB is an in-memory, object-oriented, ACID database for .NET. You define the model as C# classes (`DatabaseObject`, references, indexes) and run business logic **inside the database process** as `[DbAPI]` operations that clients call over the VeloxDB protocol.

How we use it:
- **Model project** (`PremestiSe.Db.Model`): VeloxDB classes and hash indexes, for example `Parent.Email` (unique), `Kindergarten.ExternalKey`, `Request` by `(CityId, Status)`, and `PendingRequest.TokenHash`.
- **DB API project** (`PremestiSe.Db.Api`): operations `SubmitPendingRequest`, `ConfirmRequest`, `RunMatching(requestId)`, `DecideMatch`, `WithdrawRequest`, `ExpireStale`, and the read queries for the map (aggregated). Matching and reservation run inside a single DB operation, so no two matches can claim the same request.
- **Contracts project** (`PremestiSe.Db.Contracts`): shared DTOs and interfaces that the ASP.NET API calls through `ConnectionFactory.Get<IRequestApi>()`.
- The ASP.NET API stays **thin**. It handles HTTP, auth tokens, CAPTCHA, rate limits, and mapping to DB API calls.

Risks and mitigations:
- *Niche ecosystem, small community.* Keep the domain logic in plain C# services that the DB operations call, so it can be unit-tested without the DB and ported if needed.
- *RAM-bound dataset.* Not a concern here: about 1.8k kindergartens and thousands to tens of thousands of requests.
- *Ops:* configure persistence (log) directories, backups, and a replication/standby node before go-live. Confirm the hosting supports it (container or VM).

### 5.4 Solution layout (proposal)

```
/src
  /PremestiSe.Api              ASP.NET Core host (REST, worker)
  /PremestiSe.Domain           Pure C#: matching algorithm, rules, value objects
  /PremestiSe.Db.Model         VeloxDB model classes
  /PremestiSe.Db.Api           VeloxDB DbAPI operations (deployed to VeloxDB server)
  /PremestiSe.Db.Contracts     Shared interfaces/DTOs
  /PremestiSe.Importer         xlsx → normalized kindergartens (+ geocoding cache)
  /web                         React app (Vite)
/tests
  /PremestiSe.Domain.Tests     Matching unit/property tests
  /PremestiSe.Api.Tests        Integration tests against a VeloxDB container
/deploy                        docker-compose, IaC
/docs
```

---

## 6. Key flows

### 6.1 Submit and confirm a request

```
SPA                     API                         VeloxDB                 Worker/Email
 │ GET /cities           │                             │                         │
 │ GET /cities/{id}/kindergartens                      │                         │
 │ POST /requests ──────►│ validate + captcha          │                         │
 │                       │ token = random(32B)         │                         │
 │                       │ SubmitPendingRequest(hash)─►│ store + outbox email    │
 │◄── 202 "check email" ─│                             │                         │
 │                       │                             │◄─ poll outbox ──────────│ send link
 │ (user clicks /confirm?token=…)                      │                         │
 │ POST /requests/confirm►│ ConfirmRequest(hash) ─────►│ Pending→Request(Active) │
 │                       │                             │ RunMatching(requestId)  │
 │◄── 200 + manage link ─│                             │ (+ outbox match emails) │
```

- The token is a 256-bit random value, **stored only as a SHA-256 hash**, single use, valid for **24h**.
- The brief says "OTP link". Use a **magic link** (one click) and optionally a 6-digit code as a fallback for mail clients that pre-fetch links. Confirming takes a **POST** from the landing page, not a GET, so link scanners can't confirm requests by accident.
- If the same email submits again while a request is pending or active: show "you already have a request" and send the manage link. No duplicate requests per child slot (see Q2).
- After confirmation the parent gets a **manage link** (a long-lived signed token) to view, edit, or withdraw the request. No password accounts are needed in v1.

### 6.2 Match notification

1. The matcher creates `Match[Proposed]` and enqueues one email per participant: "You have a potential swap. Accept or decline by <date>."
2. When everyone accepts, the match becomes `Accepted` and each participant gets an email with the **other participants' names, emails, and phones**, the chain order (who moves where), and the next steps at the PU.
3. Participants can mark the match "Completed" (the transfer happened). Otherwise it auto-closes after N days.

### 6.3 Email delivery (transactional outbox)

DB operations never call SMTP directly. They write `EmailOutbox` rows in the **same transaction**. The worker sends them with retries and backoff, then marks them sent. This guarantees that "match created" means "email will be sent".

Templates are in sr-Cyrl and sr-Latn, with plain-text alternatives. Use a provider with SPF/DKIM/DMARC configured on the sending domain.

### 6.4 Map

- **Import time:** geocode every kindergarten once (Nominatim/OSM with a polite rate limit, or a paid geocoder for accuracy). Store `Lat`, `Lng`, and `GeoPrecision` (`address` / `street` / `settlement`). Allow manual corrections, and keep a geocoding cache file in the repo so re-imports are deterministic.
- **Runtime:** `GET /cities/{id}/map` returns kindergartens with **aggregated, anonymous counts**: `{kgId, name, lat, lng, outgoing: n, incoming: n, topDestinations: [...]}`. **No personal data is ever on the map.**
- **UI:** Leaflet with OSM tiles and marker clustering. Marker colour or size shows demand. Clicking a kindergarten shows "N parents want to leave / M want to join", and arrows to popular destinations. Clicking "I can help" opens the form pre-filled with that kindergarten as destination or source.
- Cache the map response for 1–5 minutes.

---

## 7. API (v1 sketch)

| Method | Path | Purpose |
|---|---|---|
| GET | `/api/cities?search=` | List cities (municipalities), with Cyrillic/Latin search |
| GET | `/api/age-groups` | List of age groups |
| GET | `/api/cities/{cityId}/kindergartens?search=&municipalityId=` | Kindergartens in a city, with an optional municipality filter (useful for Belgrade) |
| GET | `/api/cities/{cityId}/map?ageGroup=` | Aggregated map data, optionally filtered by age group |
| POST | `/api/requests` | Create a PendingRequest (CAPTCHA required) → 202 |
| POST | `/api/requests/confirm` | `{token}` → activate the request, trigger matching |
| POST | `/api/requests/resend-confirmation` | Rate limited |
| GET | `/api/requests/me` | With the manage token: view own request and its status |
| PATCH | `/api/requests/me` | Change destination(s) or phone |
| DELETE | `/api/requests/me` | Withdraw the request and erase personal data |
| GET | `/api/matches/{id}` | With a participant token: view a match (contact details only once Accepted) |
| POST | `/api/matches/{id}/decision` | `{accept: bool}` |
| POST | `/api/matches/{id}/complete` | Mark the transfer done |
| GET | `/api/health` | Liveness and readiness (includes VeloxDB connectivity) |

Errors use RFC 7807 `ProblemDetails`. The API is versioned under `/api/v1` from the start.

---

## 8. Security and privacy

The platform processes personal data about parents and, indirectly, children. Serbia's **Zakon o zaštiti podataka o ličnosti (ZZPL)** is GDPR-aligned.

- **Data minimisation:** collect no child name or birth date in v1, only the age group.
- **Consent:** an explicit checkbox explaining that contact details will be shared with matched participants. Link to the privacy policy.
- **Visibility:** contact data is shared **only with participants of an Accepted match**, and never shown on the map or in public APIs.
- **Retention:** unconfirmed pending requests are deleted after 24h. Active requests expire after 90 days, with a reminder email to extend. Withdrawn or expired requests are anonymised after 30 days. Completed matches are anonymised after 90 days.
- **Right to erasure:** the "Withdraw and delete my data" action uses the manage link.
- **Abuse protection:** CAPTCHA on submit, per-IP and per-email rate limits, disposable-email blocklist (optional), and request caps (1 active request per email and child, see Q2).
- **Tokens:** random values, stored hashed, scoped (confirm, manage, match), and expiring.
- **Transport and storage:** HTTPS only (HSTS), encrypted backups, secrets in a vault or environment variables, never in the repo.
- **Audit log** for confirm, match, decision, and erasure events.
- **v2 KYC:** SMS OTP to the phone number, and possibly eID / Consent ID (eUprava) for higher assurance.

---

## 9. Non-functional requirements

| Area | Target |
|---|---|
| Availability | 99.5% (single region, with a standby VeloxDB node) |
| Latency | p95 < 300 ms for API reads, < 1 s for submit/confirm |
| Matching | A new active request is evaluated within 5 s. The full sweep takes < 1 min |
| Scale | ~2k kindergartens and ≤ 100k requests fit comfortably in memory |
| Accessibility | WCAG 2.1 AA, mobile-first (most parents will use phones) |
| Localization | sr-Cyrl (default), sr-Latn, en |
| Observability | Structured logging (Serilog), OpenTelemetry traces and metrics, alerts on outbox backlog and email bounces |
| Testing | Unit tests for the matcher, including property-based cycle checks. API integration tests run against a VeloxDB container. Playwright E2E for the form → confirm → match flow |

---

## 10. Deployment

- **Environments:** dev (docker-compose: api + veloxdb + mailpit), staging, prod.
- **CI/CD:** GitHub Actions. Build and test, deploy the VeloxDB model and DB API assemblies to the VeloxDB server, deploy the API container, deploy the SPA to static hosting or a CDN.
- **Hosting:** a container platform or VM in an EU region (Azure, Hetzner, or AWS). VeloxDB needs persistent volumes for its logs and snapshots.
- **Schema evolution:** use VeloxDB model versioning, apply model updates through the deployment pipeline, and keep the importer idempotent.

---

## 11. Open questions / decisions needed

| # | Question | Recommendation |
|---|---|---|
| Q1 | Should a swap require the **same age group**? | ✅ **Decided:** yes. Swaps happen only within the same age group |
| Q2 | Can one parent submit requests for **several children**? | Open. Recommendation: 1 active request per (email, child label). v1: one per email |
| Q3 | Is "City" the **municipality** (`opstina`) or the region (`uprava`)? | ✅ **Decided:** the municipality, with all Belgrade municipalities grouped into one city "Београд" |
| Q4 | Can matches **cross cities**? | Open. Recommendation: same city only in v1, controlled by config. Belgrade is one city, so swaps across its municipalities work |
| Q5 | Do **all participants have to accept** before a match is final and contact details are shared? | ✅ **Decided:** yes (see §4.3) |
| Q6 | Include the Kosovo administrations (Ранилуг, Косовска Митровица)? | ✅ **Decided:** no, removed for now. They can be re-enabled later through config (`Import:ExcludedRegions`) |
| Q7 | Does the brief's "OTP link" mean a magic link or a code? | A magic link plus a fallback code |
| Q8 | Private kindergartens are not in the dataset. Are they in scope? | Out of scope for v1 |
| Q9 | Should the registry data be refreshed automatically (e.g. from data.gov.rs)? | Manual re-import at first |
| Q10 | Is a moderator or admin role needed in v1? | A minimal CLI admin only |
