# QuickCommands Server – Design

Backend for QuickCommands' **Cloud Save** and **Store**. The desktop app only talks to the server if the user
explicitly turns it on (opt-in). This document records the decisions made before implementation and describes how
the server is put together.

## Overview

| Topic | Decision |
|---|---|
| Location | `server/` in the QuickCommands repo, with its own `QuickCommands.Server.slnx`, `Directory.*.props` and workflow |
| Runtime | .NET 10, ASP.NET Core with **controllers** |
| Structure | **one** project with a clear folder layout (see below) |
| Database | MySQL 8.4, EF Core 10 with `Microting.EntityFrameworkCore.MySql` (MIT, fork of Pomelo) |
| Migrations | code-first, applied automatically on startup (`Database__MigrateOnStartup`, default `true`) |
| Auth | Firebase ID token as a JWT (`AddJwtBearer`), no Firebase Admin SDK |
| Validation | FluentValidation through a global `ValidationFilter`, DataAnnotations off |
| Logging | Serilog → console + a custom sink to **loggerui** (project `quickcommands`) |
| API | REST under `/api/v1`, `GET /api/health` is anonymous |
| Docker | multi-stage, `aspnet:10.0-noble-chiseled`, non-root, port 8080, healthcheck via `--healthcheck` |
| CI | GitHub Actions → GHCR, tags `YEAR.MONTH.build` and `latest` |

Why Microting and not Pomelo or Oracle: Pomelo has no release for EF Core 10 (the last one is 9.0.0 and the
project is dormant). Oracle's `MySql.EntityFrameworkCore` is GPLv2 (with the FOSS exception), which comes with
obligations for a public image (license text, offer of source code). Microting is MIT, API-compatible with Pomelo
and has stable 10.x releases.

## Project layout

```
server/
  QuickCommands.Server.slnx
  Directory.Build.props / Directory.Packages.props   independent of the desktop props
  Dockerfile, .dockerignore
  compose.dev.yml          just MySQL, for local development
  compose.example.yml      template for the Docker environment
  openapi/v1.json          generated on build, CI checks that it's up to date
  docs/DESIGN.md           this document
  src/QuickCommands.Server/
    Controllers/       thin: call the service, return the result
    Entities/          EF entities (never exposed directly)
    Dtos/Requests/     API input
    Dtos/Responses/    API output
    Mapping/           entity → DTO, by hand (extension methods)
    Interfaces/        IStoreService, ICloudSaveService, ICurrentUser, ...
    Services/          logic, uses AppDbContext directly (no repository layer)
    Validators/        FluentValidation per request + shared rules
    Middlewares/       exceptions → ProblemDetails, request ID, current user
    Filters/           ValidationFilter
    Data/              AppDbContext, Configurations/, Migrations/
    Options/           strongly typed configuration
    Exceptions/        AppException and subclasses (→ status code + code)
    Logging/           LoggerUi sink
  tests/QuickCommands.Server.Tests/   xUnit v3, unit and integration tests (Testcontainers MySQL 8.4)
```

Rules:
- Controllers contain no logic. Errors are thrown as `AppException` and translated by the
  `ExceptionHandlingMiddleware`.
- Services work with `AppDbContext` directly. EF Core already is a repository and unit of work.
- Entities never leave the server; only DTOs from `Dtos/Responses` go out.

## API conventions

- JSON: camelCase, enums as camelCase strings (`powerShell`, `cmd`, `sh`), `null` is omitted.
- IDs: `Guid`, created on the server with `Guid.CreateVersion7()` (time-ordered, which is good for the index).
- Times: UTC, ISO 8601.
- Paging: `page` (starting at 1) and `pageSize` (default 20, max 50). Response: `{ items, page, pageSize, totalCount }`.
- Errors: always `application/problem+json` with `status`, `title`, `detail`, `code` (machine-readable) and `traceId`.
  Validation errors also include `errors`.
- Versioning: all routes live under `/api/v1`. Breaking changes go under `/api/v2` as long as old clients are still around.

### Error codes

| Status | `code` | Meaning |
|---|---|---|
| 400 | `validation_failed` | invalid request, details in `errors` |
| 400 | `unknown_category` | category slug doesn't exist |
| 400 | `invalid_document` | Cloud Save document is not a JSON object with an integer `schemaVersion` ≥ 1 |
| 401 | `unauthorized` | token missing or invalid |
| 403 | `forbidden` | not allowed (e.g. someone else's package, not an admin) |
| 403 | `user_banned` | user is banned |
| 404 | `not_found` / `package_not_found` / `user_not_found` / `cloud_save_not_found` / `revision_not_found` | doesn't exist or isn't visible |
| 409 | `handle_required` | a handle has to be set before publishing for the first time |
| 409 | `handle_taken` | handle is taken or reserved |
| 409 | `package_locked` | package is hidden or removed and can't be edited |
| 409 | `already_reported` | user has already reported this package |
| 409 | `cannot_report_own` | you can't report your own package |
| 409 | `package_deleted` | admin restore of a package whose author already deleted it (only the tombstone is left) |
| 412 | `revision_mismatch` | Cloud Save was changed from another device in the meantime |
| 413 | `payload_too_large` | Cloud Save document too large |
| 428 | `precondition_required` | `If-Match` or `If-None-Match` missing |
| 429 | `rate_limited` | too many requests |
| 500 | `internal_error` | unexpected error (logged as `error`) |

## Authentication

- The desktop client signs in with Firebase directly and sends the **ID token** as `Authorization: Bearer ...`.
- The server validates the token as a JWT: authority `https://securetoken.google.com/<projectId>`, audience
  `<projectId>`. The signing keys come from Google via OpenID discovery. No service account key needed.
- The Firebase UID (`sub`) identifies the user. On the first authenticated request the `CurrentUserMiddleware`
  creates a row in `users`. `LastSeenAt` is updated at most once an hour.
- Banned users get `403 user_banned`. The exceptions are `GET /me` and `DELETE /me`, so a banned person can still
  delete their data.
- A user is an admin if their UID is listed in `Store__AdminUids` (comma-separated).

## Endpoints

Legend: 🌐 anonymous, 🔒 signed in, 🛡️ admin.

### System
| | Method | Path | Description |
|---|---|---|---|
| 🌐 | GET | `/api/health` | `{ status, version, database }`, 503 if the database can't be reached |
| 🌐 | GET | `/api/v1/categories` | fixed list of categories `{ slug, nameDe, nameEn }` |

### Me
| | Method | Path | Description |
|---|---|---|---|
| 🔒 | GET | `/api/v1/me` | own profile `{ id, handle, isAdmin, isBanned, createdAt }` |
| 🔒 | PUT | `/api/v1/me` | set or change `{ handle }` (3–20 characters, `a-z0-9_-`, reserved names blocked) |
| 🔒 | GET | `/api/v1/me/packages` | own packages in every status except deleted |
| 🔒 | DELETE | `/api/v1/me` | delete account data (see below) |

### Cloud Save
The server stores **one JSON document per user** and doesn't know its schema. Merging happens in the client.
The document has to be a JSON object with a `schemaVersion` field (integer ≥ 1) and can be at most 1 MB.

| | Method | Path | Description |
|---|---|---|---|
| 🔒 | GET | `/api/v1/cloud-save` | current document, header `ETag: "<revision>"`, 404 if nothing has been saved yet |
| 🔒 | PUT | `/api/v1/cloud-save` | save the document. Requires `If-Match: "<revision>"`, or `If-None-Match: *` the first time. Response `{ revision, schemaVersion, sizeBytes, createdAt }` + new `ETag` |
| 🔒 | GET | `/api/v1/cloud-save/revisions` | the last 10 revisions (metadata only) |
| 🔒 | GET | `/api/v1/cloud-save/revisions/{revision}` | content of an older revision |
| 🔒 | DELETE | `/api/v1/cloud-save` | turn off Cloud Save: delete all revisions |

Client flow: `GET` → merge locally → `PUT` with `If-Match`. On `412`, reload, merge again and retry. To restore,
the client loads an old revision and saves it with `PUT` as a new revision. The server keeps the last 10 revisions.
A unique index stops two concurrent `PUT`s on the same revision: one wins, the other gets `412`.

### Store
A **package** contains 1–20 commands. A single command is just a package with one entry.

- Package: title (3–80), description (≤ 2000), **exactly one category** (fixed slug), **0–5 tags**
  (normalized: lowercase, spaces → `-`, `a-z0-9-`, 2–24 characters), `version`, stars, installs, author handle.
- Command: name (same rules as the desktop app: `^[a-z0-9][a-z0-9_-]*$`, ≤ 64, not `qc`), language (`powerShell`,
  `cmd`, `sh`), description (≤ 500), script (1 character up to 64 KB), `parameters` (any JSON array, ≤ 16 KB, the
  server just stores it). Name + language have to be unique within a package. Mixing languages is allowed.

| | Method | Path | Description |
|---|---|---|---|
| 🌐 | GET | `/api/v1/store/packages` | search: `q`, `category`, `tag`, `language`, `sort`, `page`, `pageSize` |
| 🌐 | GET | `/api/v1/store/packages/versions?ids=...&ids=...` | update check for up to 100 packages |
| 🌐 | GET | `/api/v1/store/packages/{id}` | details including all scripts. When signed in, also `starredByMe` and `installedByMe` |
| 🔒 | POST | `/api/v1/store/packages` | publish (handle required), 201 + `Location` |
| 🔒 | PUT | `/api/v1/store/packages/{id}` | edit (author only): replaces everything, `version + 1` |
| 🔒 | DELETE | `/api/v1/store/packages/{id}` | delete (author or admin) → tombstone |
| 🔒 | PUT / DELETE | `/api/v1/store/packages/{id}/star` | add or remove a star (idempotent) |
| 🔒 | POST | `/api/v1/store/packages/{id}/install` | count an install (once per user, idempotent) |
| 🔒 | POST | `/api/v1/store/packages/{id}/reports` | report: `{ reason, comment? }` |

**Visibility:** search and details only show active packages. The author and admins can still see hidden or
removed packages in the details view. Everyone else gets `404`.

**Sorting (`sort`):**
- `popular` (default): `(stars * 3 + installs + 1) / (age in hours + 2)^1.5`, ties go to the newer package.
- `stars`, `installs`, `new` (by creation), `updated` (by last change).

`StarCount` and `InstallCount` are kept as counters on the package and updated in the same transaction as the
star or install. Ranking happens directly in SQL, no background job. Every counter change (star, install, report)
first locks the package row with `SELECT … FOR UPDATE`, so concurrent requests queue up instead of deadlocking on
MySQL's foreign-key locks.

**Search (`q`):** substring match on title, description, tags and command names.

**Update check (`/versions`):** returns `{ id, status, version?, removalReason? }` for each known ID. Unknown IDs
are left out.

| Status | Meaning | What the client should do |
|---|---|---|
| `active` | normal, `version` is set | offer an update if the version is higher |
| `hidden` | temporarily hidden after 3 reports | show "under review" |
| `removed` | removed by an admin, `removalReason` is set | for `malicious`, **warn clearly** and ask the user to check or delete it; otherwise a neutral notice |
| `deleted` | deleted by the author or together with their account | show "no longer in the store", the local command stays |

**Tombstones:** deleted packages aren't removed from the database. Only the ID, status, `removedAt` and
`removalReason` are kept. Title, description, commands, tags, stars, installs, reports and the link to the author
are deleted. A package that had already been removed (`removed`) keeps its status and reason, so the `malicious`
warning survives deletion. Tombstones are never cleaned up.

### Moderation
- Report reasons: `malicious`, `spam`, `broken`, `inappropriate`, `other`, plus an optional comment (≤ 1000).
  Once per user and package.
- **Every report** is logged as `warn`, which triggers the loggerui alert (see Logging).
- Once a package has **3 open reports** from different users it's automatically set to `hidden`. That's logged
  as `warn` too.

| | Method | Path | Description |
|---|---|---|---|
| 🛡️ | GET | `/api/v1/admin/reports?open=true&page=&pageSize=` | reports with package, author and reporter |
| 🛡️ | POST | `/api/v1/admin/packages/{id}/remove` | `{ reason }` → `removed`, open reports are resolved |
| 🛡️ | POST | `/api/v1/admin/packages/{id}/restore` | back to `active`, open reports are closed as unfounded |
| 🛡️ | POST | `/api/v1/admin/users/{id}/ban` | `{ reason, removePackagesWithReason? }` bans the user; if a reason is given, all their active/hidden packages are removed with it |
| 🛡️ | POST | `/api/v1/admin/users/{id}/unban` | lift the ban |

### Deleting an account (`DELETE /api/v1/me`)
This deletes the user's Cloud Save with all revisions, their stars (the affected packages' counters go down),
installs and reports. The user's packages become tombstones (see above). People who installed them keep the
command, and the update check reports `deleted`. The user row is deleted. If the user is banned, only the UID and
the ban are kept, so signing up again stays blocked. The client deletes the Firebase account itself through the
Firebase SDK.

## Data model

```
users                (id, firebase_uid UNIQUE, handle UNIQUE NULL, created_at, last_seen_at, is_banned, banned_at, ban_reason)
cloud_save_revisions (id, user_id, revision, schema_version, size_bytes, content, created_at)  UNIQUE(user_id, revision)
categories           (id, slug UNIQUE, name_de, name_en, sort_order)                              seeded
store_packages       (id, owner_id NULL, title, description, category_id NULL, version, status, removal_reason NULL,
                      star_count, install_count, open_report_count, created_at, updated_at, removed_at NULL)
store_commands       (id, package_id, sort_order, name, language, description, script, parameters_json NULL)
store_package_tags   (package_id, tag)             PK(package_id, tag), INDEX(tag)
store_stars          (package_id, user_id, created_at)   PK(package_id, user_id)
store_installs       (package_id, user_id, created_at)   PK(package_id, user_id)
store_reports        (id, package_id, reporter_id, reason, comment, created_at, resolved_at NULL, resolution NULL)
                      UNIQUE(package_id, reporter_id)
```

Categories (seed): `git`, `navigation`, `files`, `docker`, `build`, `dev-tools`, `network`, `system`, `other`.
New categories are only added through a migration.

The Cloud Save document is stored as text on purpose, not as MySQL's `JSON` type. MySQL would reformat a JSON
column and sort the keys, but the client should get back exactly what it saved.

## Middlewares and pipeline

Order:
1. `RequestIdMiddleware`: takes `X-Request-Id` or generates a new ID, returns it in the response and adds it to the log context.
2. Serilog request logging: one `info` line per request with `method`, `path`, `statusCode` and `elapsedMs` (also for 5xx – the error itself is logged once, with stack trace, by the next middleware).
3. `ExceptionHandlingMiddleware`: `AppException` → matching status + `code`, anything else → 500 + `error` log. The stack trace is only included in the response in Development (and in the `Testing` environment of the integration tests).
4. Forwarded headers (nginx sits in front), status code pages as ProblemDetails.
5. Authentication → `CurrentUserMiddleware` (upsert, ban check) → rate limiter → authorization → controller.

Rate limits (configurable under `RateLimits`):
- **global:** 300 requests/minute per user or IP
- **`write`:** 30/minute per user, applies to publish, edit, delete, star, install, report and Cloud Save `PUT`

## Logging → loggerui

- Serilog writes to the console (for `docker logs`) and, through `LoggerUiSink`, as JSONL via
  `POST <LoggerUi__Url>/api/logs?project=quickcommands` with `Authorization: Bearer <LoggerUi__Token>`.
- The sink uses Serilog's batching: at most 200 events or 2 s per request, with a buffer of 10,000 events. When
  the buffer is full, new events are dropped. If loggerui can't be reached, Serilog retries later. The API itself
  is never affected.
- **Line:** `{ type, timestamp (Unix ms), description, stacktrace?, origin (hostname), metadata }`.
  loggerui rejects any other field. String values are rendered into `description` without quotes.
- Problems of the sink itself (wrong token, rejected batch, loggerui unreachable) are written to stderr via
  Serilog's `SelfLog`, so they show up in `docker logs`.
- **metadata:** flat, at most 50 keys, strings at most 2000 characters. Typical keys: `requestId`, `traceId`,
  `env`, `version`, `path`, `statusCode`, `elapsedMs`, `uid`, `source` plus the log message's properties.
  **Never** log email addresses, tokens or scripts.
- **Level discipline**, because loggerui alerts on every `warn`:
  - `warn`: only reports (`Package reported`) and automatic hiding
  - `error`: only unexpected errors / 500s
  - anything expected (validation, 404, 412, rate limit, invalid tokens) is `info`
  - `Microsoft.*` and `System.*` only go to loggerui from `Error` up
- **loggerui setup:** create a project `quickcommands`, generate an upload token for it, add an alert on level
  `warn` with mode `all`, webhook to Discord or ntfy. loggerui groups multiple alerts within the cooldown
  (default 10 min) into one.

## Configuration

| Variable | Required | Description |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | yes | e.g. `Server=mysql;Port=3306;Database=quickcommands;User=quickcommands;Password=...` |
| `Database__ServerVersion` | no | default `8.4.0-mysql` |
| `Database__MigrateOnStartup` | no | default `true` |
| `Firebase__ProjectId` | yes | ID of the Firebase project |
| `Store__AdminUids` | no | Firebase UIDs of the admins, comma-separated |
| `LoggerUi__Url` | no | e.g. `https://logging.example.com`. Without a URL, logs only go to the console |
| `LoggerUi__Token` | no | upload token of the `quickcommands` project |
| `LoggerUi__Project` | no | default `quickcommands` |
| `RateLimits__GlobalPerMinute` / `RateLimits__WritePerMinute` | no | default 300 / 30 |
| `ForwardedHeaders__KnownProxies` | no | IPs of the reverse proxy, comma-separated (default: loopback and private networks) |

The MySQL database `quickcommands` gets its own user with rights on that database only, including DDL, because
migrations run on startup.

## Operations

- The image `ghcr.io/felixkriechbaum/quickcommands-server` is attached to the external network `internal_com`,
  where MySQL is reachable as `mysql:3306`.
- It binds `127.0.0.1:3005:8080`. nginx on the host forwards the domain to that port (see `compose.example.yml`).
- The healthcheck needs no shell and no curl: `dotnet QuickCommands.Server.dll --healthcheck` calls
  `http://localhost:8080/api/health` and exits with code 0 or 1.

## CI and versioning

Workflow `.github/workflows/server.yml`, only runs on changes under `server/**`:
- **Pull request:** build, tests (including Testcontainers), check that `openapi/v1.json` is up to date.
- **Push to `main`:** additionally builds the image (amd64 + arm64) and pushes it to GHCR with the tags
  `YEAR.MONTH.run_number` (e.g. `2026.11.57`) and `latest`, plus the Git tag `server/<version>`. The version ends up
  in the assembly, in `/api/health` and in the log metadata (`version`).
- Nothing is pushed unless all tests pass.

## Open / later

- The desktop app doesn't know about the server yet. Opt-in, Firebase sign-in, merge logic and the API client
  (generated from `openapi/v1.json`) come in a separate step.
- Maybe later: a list of your own stars, a full-text index for search, a moderation UI.
