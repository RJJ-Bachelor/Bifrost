# Project Context: Bifrost (Used and updated by the ReviewBot)

## 1. Overview & Purpose
Bifrost is a classroom communication and interaction platform designed to facilitate real-time engagement between teachers and students. Developed as a software engineering bachelor's project by Jonas, Jakob, and Rune, the system is architected around Clean Architecture, Domain-Driven Design (DDD), and event-driven microservice/modular-monolith patterns to ensure high scalability, loose coupling, and clear domain boundaries.

Key capabilities:
- **Session Management:** Teachers create, monitor, clear (incrementing generation), archive, and delete interactive sessions identified by unique 8-character codes. Sessions feature distinct student (`Code`) and teacher (`TeacherCode`) access codes.
- **Student Participation:** Students join active sessions anonymously using cryptographically signed session cookies, without requiring permanent user accounts.
- **Help Requests:** Students submit help requests tied to active sessions and specific session generations, which are processed, tracked, and answered. Requests include original questions, follow-up messages, and system-generated FAQ content. Teacher reservations are atomic and bound to the individual claiming teacher while remaining visible across all teachers joined to the session.
- **Asynchronous Intelligence & Notifications:** Distributed background services handle AI request generalization, real-time message routing, SignalR-based notifications, and analytics.
- **FAQ Management:** Teachers manage session-scoped FAQs via create/edit/delete operations; generalizations from answered help requests feed into the KnowledgeBase module for future reference.

---

## 2. Tech Stack & Key Technologies
- **Backend Runtime & Language:** .NET 10 (`net10.0`), C# 14 (SDK default — no explicit `LangVersion`).
- **Orchestration & Diagnostics:** .NET Aspire (`YggdrasilService.AppHost` & `YggdrasilService.ServiceDefaults`) orchestrating PostgreSQL databases, RabbitMQ broker, OpenTelemetry metrics, traces, and health checks.
- **API Framework:** FastEndpoints 8.3.0 (REPR pattern: Request-Endpoint-Response) for API endpoints, internal in-process commands, and domain event notifications. Global serializer uses `JsonStringEnumConverter` for string-based enum serialization.
- **Validation:** FluentValidation 12.2.0 integrated into FastEndpoints via pipeline command middleware (`CommandValidationMiddleware`).
- **Gateway & Reverse Proxy:** YARP (Yet Another Reverse Proxy) in `HeimdallGateway` managing edge routing, token extraction, and student cookie issuance.
- **Identity & Authentication:**
  - **Teachers:** Duende IdentityServer (in `VarService`) with ASP.NET Core Identity for OAuth 2.0 / OpenID Connect authorization code flow with PKCE and JWT Bearer tokens.
  - **Students:** ASP.NET Core Data Protection encrypted cookie authentication (`bifrost-anonymous-user`) issuing anonymous student identifiers.
- **Databases & Persistence:**
  - PostgreSQL 16+ via Entity Framework Core 10 (`10.0.14`) and Npgsql EF provider (`10.0.3`).
  - Isolated database per service (`eir`, `gna`, `mimir`, `var`).
  - Isolated database schema per module in `EirService` (`sessions`, `participants`, `helprequests`, `knowledgebase`).
- **Message Broker & Event-Driven Architecture:**
  - RabbitMQ 4+ (`RabbitMQ.Client 7.2.2`).
  - Custom abstraction `IMessageBus` (`RabbitMqMessageBus`) with W3C distributed trace context propagation via OpenTelemetry `ActivitySource`.
  - Transactional Outbox Pattern (`OutboxDispatcher`, `OutboxMessage`, `OutgoingMessage`) for reliable at-least-once message dispatching.
  - Unit of Work pattern (`IUnitOfWork`, `IUnitOfWork<TContext>`) guaranteeing atomic persistence of aggregate changes and outbox messages.
  - Outbox coalescing (`OutgoingMessage.Coalesce`) — keyed dedup within a single commit; highest `Priority` wins (see §5).
- **Real-time Notifications:** SignalR hubs (`/api/students/notifications`, `/api/teachers/notifications`) in `GnaService`, supporting both student identity cookies and query-string JWT bearer tokens (`access_token`).
- **Frontend:** Angular 22 (`glitnir-frontend`) — standalone components, signals, zoneless change detection, Vitest. Orchestrated via Aspire (`.esproj` / `AddJavaScriptApp`). See §3.3 for architecture, auth, routing, realtime, and conventions.
- **CI/CD:** GitHub Actions (automated builds, tests, and automated AI Pull Request code reviewer bot).

---

## 3. Architecture & Dependency Rules

### 3.1 Clean Architecture Layers
Every service and module strictly adheres to Clean Architecture:
- **Domain Layer:** Pure business models, aggregates, entities, value objects, and domain events. Zero external framework or infrastructure dependencies (only depends on `Shared.Domain`).
- **Application Layer:** Use cases, commands, queries, event handlers, domain event notifications, repository interfaces, and DTOs. Depends strictly on Domain and `Shared.Application`.
- **Infrastructure Layer:** EF Core `DbContext`, repository implementations, RabbitMQ subscribers, outbox dispatchers, and external integrations. Depends on Application and Domain.
- **Presentation / API Layer:** FastEndpoints endpoint definitions, API routing, custom middleware, and authentication handlers. Depends on Application and Infrastructure.

### 3.2 Fundamental Architectural Rules
1. **Inward Dependencies:** Dependencies must always point inward. Controllers and endpoints must never execute raw database queries or bypass application use cases.
2. **Modular Monolith Boundaries (`EirService`):**
   - Each module (`Sessions`, `Participants`, `HelpRequests`, `KnowledgeBase`, `Insights`) is an isolated bounded context.
   - Modules must **never** reference another module's Domain or Infrastructure project.
   - Modules must **never** perform cross-schema database queries or share foreign keys.
   - In-process cross-module communication is permitted **only** through `EirService.Contracts` via FastEndpoints command/query bus (`ICommand<TResult>`).
   - Asynchronous cross-module communication must take place via RabbitMQ integration events emitted through the transactional outbox.
3. **Transactional Outbox Mandate:**
   - Any database mutation that produces an external message must write an `OutboxMessage` in the same database transaction. Direct publishing to `IMessageBus` during request processing is strictly forbidden.
4. **Idempotent Message Processing:**
   - All RabbitMQ message subscribers must handle redeliveries gracefully without duplicating state or causing fatal errors.
5. **Language Rule:**
   - All **developer-facing** code artifacts must be written **100% in English**: identifiers, symbols, class/method/variable names, code comments, log messages, exception messages, commit messages, and documentation.
   - **User-facing text is Danish** and is not subject to the English rule. This includes UI labels, button text, error/status messages rendered in the UI (whether in `.html` templates or as string literals in `.ts`/`.cs` passed to a view), placeholder text, and validation messages shown to end users.
   - **Test data** (hardcoded test questions, fixtures, mock content) may be Danish.

### 3.3 Frontend Architecture

Feature-based with three layers: **`core/`** (global singletons: auth, layout, guards, realtime; no feature knowledge), **`shared/`** (UI-only components: `Card`, `Dialog`, `ListRow`, `StatusDialog`, `StatTile`), **`features/`** (`session`, `faq`, `help-request`, `student`, `teacher`, `not-found` — isolated, own services/components/models).

**Conventions:**

- `@Service()` (Angular v22 shorthand) for singleton services/stores; `.model.ts` suffix for models (Angular exception to "no type suffixes").
- Features are isolated — cross-feature imports only via each feature's `index.ts` barrel; no deep imports into another feature's internals.
- Selector prefix `bifrost` (enforced in `angular.json`); never `app-`.
- State via signals + `httpResource`/`HttpClient` for cookie-backed calls (`withCredentials: true`). Layout utilities (`surface-box`, `icon-mask`, `input-field`, `form-error`) in `styles/_utilities.scss` — compose via class lists. Topbar per page (not global in `app.html`) with named content-projection slots.
- Forms use signal-based `@angular/forms/signals` (`form`, `FormField`, `validate`, `required`) — never Reactive or Template-driven. `withComponentInputBinding()` binds `:id`/`:requestId` to component `input()` signals — no `ActivatedRoute` snapshots.
- **No legacy Angular patterns:** no `NgModule`, no constructor injection (use `inject()`), no `*ngIf`/`*ngFor` (use `@if`/`@for`), no Karma/Jasmine (Vitest only).
- Dev proxy (`proxy.conf.json`) forwards `/api/**` (incl. WS) and `/account/**` to `http://localhost:6004`. `tsconfig.json` lacks `strict: true` but enables `noImplicitOverride`/`noImplicitReturns`/`noFallthroughCasesInSwitch`/`noPropertyAccessFromIndexSignature` + Angular strict injection/input.

**Auth, routing & realtime:**

- Cookie-based OAuth via HeimdallGateway (`bifrost-teacher`) + anonymous student cookies (`bifrost-anonymous-user`). Identity loaded lazily per guard. `studentGuard` always `true` except under `/teacher/*`; `teacherGuard` calls `loadIdentity('teacher')` and redirects to `${environment.authBaseUrl}/account/login?returnUrl=…` otherwise. Roles lowercased; `teacher` wins if both present. `authBaseUrl` hardcoded to `http://localhost:6004` in both env files (known debt).
- `CanMatchFn` guards short-circuit lazy loading. Student routes: `/`, `/sessions/:id`, `/sessions/:id/queue`. Teacher routes: `/teacher`, `/teacher/sessions/:id`, `/teacher/sessions/:id/dashboard`, `/teacher/sessions/:id/answer/:requestId`.
- Role-based theme via `.theme-teacher` host binding on root `App`; favicon switches on `isTeacher()`.
- `RealtimeHub` (`@Service()`) exposes `status` (signal) + `notifications$` (RxJS `Subject`, not a signal); listens only to `NotificationReceived`. Connect/disconnect driven by an `effect()` in root `App` on `authStore.role()` — guards never touch SignalR. Pages react by checking `eventType`/`change` and calling targeted `httpResource.reload()` (no bitmasks/flags).

**Session views:**

- **Teacher (`features/teacher/pages/session`):** `TeacherSession` orchestrates queue reservations (`takeNext`), archiving, statistics, FAQ CRUD. Dialogs: `SessionSettingsDialog` (codes, rename — placeholder until BF-47, delete trigger), `SessionDeleteDialog`, `ArchiveConfirmDialog`; FAQ dialogs in `features/faq/components`: `FaqFormDialog` (create/edit via `FaqDialogState` union — `closed`/`create`/`edit`), `FaqDeleteDialog`. Parent owns cross-dialog state; dialog-local state uses `linkedSignal`.
- **Student (`features/student/pages/session`):** `StudentSession` — queue status, question history, FAQ archive, message form; `FaqArchiveList` takes a dynamic `maxItems`.

**Testing:**

- All services and non-trivial logic require unit tests; pure shell components exempt. Dialog components with logic are tested in isolation via Vitest + TestBed. Shared Vitest helpers in `src/test-utils/` (`fake-resource.ts`, `notification.ts`) should be reused.
- jsdom lacks `<dialog>.showModal()`/`.close()`; tests stub these on `HTMLDialogElement.prototype` in `beforeAll`.

---

## 4. System Services & Modules Breakdown

### 4.1 HeimdallGateway (Edge API Gateway)
- **Role:** Central entry point for external traffic (port 6004). Routes incoming traffic via YARP to backend services.
- **Authentication Handling:**
  - `/api/students/**`: Requires `Student` authorization policy. The `StudentIdentityMiddleware` automatically intercepts requests missing the `bifrost-anonymous-user` cookie (Data Protection–encrypted), mints it, and forwards the authenticated request downstream.
  - `/api/teachers/**`: Requires `Teacher` authorization policy. Accepts Bearer JWT tokens or reads the `bifrost-teacher` cookie (OIDC session) and attaches the access token as a `Bearer` header to downstream requests.
  - `/api/monitoring/**`: Anonymous access pass-through to backend health/monitoring endpoints.
  - `/account/login` and `/account/logout`: OIDC login/logout challenge endpoints interacting with `VarService`.

### 4.2 VarService (Identity & Access Management)
- **Role:** Dedicated Duende IdentityServer instance (port 6003) backed by ASP.NET Core Identity and PostgreSQL (`var` database).
- **Clients & Scopes:**
  - `interactive`: Code flow with PKCE for teacher web interfaces (scopes: `openid`, `profile`, `roles`, `Bifrost`, plus unused placeholder `scope2`). Redirect URIs target `http://localhost:6004/signin-oidc` (plus legacy `https://localhost:44300`/`:51004`).
  - `m2m.client`: Client credentials (scope: `Bifrost`).
  - API scope `Bifrost` exposes claims `name` + `role`.
- **Seed users** (password `Pass123$`):
  - `knud`, `bob` — default role `Teacher`.
  - `learner` — role `Student`, seeded only in Development.

### 4.3 EirService (Core Modular Monolith)
- **Role:** Central domain service (port 6000) managing classroom sessions, participants, help requests, and the knowledge base.
- **Modules:**
  1. **Sessions Module (`sessions` schema):**
     - **Aggregate:** `Session` (Title, 8-char `Code` and distinct `TeacherCode` — both `SessionCode` value objects generated with `RandomNumberGenerator` from the alphabet `ABCDEFGHJKLMNPQRSTUVWXYZ23456789` (excludes `I`/`O`; **not** Crockford Base32), `CreatedByTeacherId`, `CreatedByDisplayName`, `CurrentGeneration`, `LastClearedAtUtc`).
     - **Domain Events:** `SessionClearedDomainEvent`, `SessionDeletedDomainEvent`, `SessionArchivedDomainEvent`, `SessionRenamedDomainEvent`.
     - **Integration Promotion:** Domain events promote to `SessionUpdatedIntegrationEvent` (`Cleared` | `Deleted` | `Archived`) and stage them into the `sessions` outbox.
     - **Lifecycle:** Clearing bumps generation and resets queue/FAQ; deletion removes the session and broadcasts to participants and teachers. Archiving stores only metadata in `SessionArchive` (`SessionId`, `Title`, `Generation`, `StartedAtUtc`, `ArchivedAtUtc`); the `jsonb` conversation snapshot is written asynchronously by the HelpRequests module (`SessionUpdated(Archived)` → `ArchiveQueueAsync` → `helprequests.ArchivedConversation.Data`). Read endpoints (`GetSessionArchive[s]`) reassemble via in-process contracts; `SessionArchiveAccess` restricts students to their own conversation + answered FAQs.
     - **Concurrency:** Generation-based concurrency validation on archive execution guarantees strict atomic lifecycle transitions under concurrent requests.
     - **Endpoints — Teacher:**
       - `POST /api/teachers/sessions` (Create)
       - `GET /api/teachers/sessions` (`GetSessions` — includes live `WaitingCount` per generation, excluding active/confirmation-waiting items)
       - `GET /api/teachers/sessions/{id}` (Details)
       - `PATCH /api/teachers/sessions/{id}` (`UpdateSession` — rename; body `{ title }`)
       - `POST /api/teachers/sessions/{id}/clear` (Clear / bump generation)
       - `POST /api/teachers/sessions/{id}/archive` (`ArchiveSession`; `ArchiveSessionCommand` accepts an optional title)
       - `POST /api/teachers/sessions/join` (Join by `TeacherCode`)
       - `DELETE /api/teachers/sessions/{id}` (Delete)
       - `GET /api/teachers/sessions/{id}/archives` (List archives)
       - `GET /api/teachers/sessions/{id}/archives/{archiveId}` (Single archive)
     - **Endpoints — Student:**
       - `GET /api/students/sessions/{id}` (Session summary by GUID — code lookup happens via Participants `POST /api/students/sessions/join`)
       - `GET /api/students/sessions/{id}/archives` (Own archive view)
       - `GET /api/students/sessions/{id}/archives/{archiveId}` (Single archive)
     - **Contracts:** Exposes `FindSessionByCodeQuery`, `GetSessionGenerationsQuery`, `GetSessionSummariesQuery`, `GetHelpRequestSessionQuery`, `GetSessionTeacherNamesQuery`.
  2. **Participants Module (`participants` schema):**
     - **Entity:** `SessionParticipant` (`Entity<SessionParticipantId>`, composite key `(SessionId, SubjectId, ParticipantRole)` via `SessionParticipantId`). Explicit `ParticipantRole` (`Student` | `Teacher`) for fine-grained permission boundaries; `DisplayName` and `JoinedAtUtc` are payload fields, not part of the key.
     - **Endpoints:**
       - Student: `POST /api/students/sessions/join` (Join session by code), `GET /api/students/sessions` (List joined sessions), `GET /api/students/me` (Student identity).
       - Teacher: `GET /api/teachers/me` (Teacher identity), `POST /api/teachers/sessions/join` (Join by `TeacherCode`; uses `JoinTeacherSessionCommand` and returns a `SessionResult`).
     - **Contracts:** Exposes `IsSessionParticipantQuery`, `IsSessionTeacherQuery`, `GetSessionParticipantIdsQuery`, `GetSessionTeacherIdsQuery`, `GetTeacherSessionIdsQuery`, `GetSessionTeachersQuery`, `RememberSessionTeacherCommand`.
  3. **HelpRequests Module (`helprequests` schema):**
     - **Aggregate:** `HelpRequest` is a conversation thread containing child `HelpRequestMessage` entities ordered by `SequenceNumber`. Fields include `StudentId` (nullable — `null` = FAQ request), `SessionId`, `Generation`, `HelpType` (`Physical` | `Digital`), `Status`, `FaqAnswerId`, `RequestNumber` (per-generation, unique via `(SessionId, Generation, RequestNumber)`), plus computed `DisplayNumber`. `HelpRequestMessageType` values: `StudentQuestion` | `FollowUpQuestion` | `FaqQuestion` | `FaqAnswer` | `TeacherAnswer`.
     - **Status Lifecycle:** `Pending` → `Active` → terminal `CompletedByPhysical` (physical) or `CompletedByDigital` (digital answer). FAQ-linked requests go `AwaitingFaq` → `CompletedByFaq` (accept) or back to queue (reject). `AwaitingConfirmation` exists as an open state. Student follow-up on a completed request reopens it via `ResetForQueue` (`Pending`, `AssignedTeacherId` cleared). Teacher cancel also uses `ResetForQueue` with `ReturnedToQueueAt` set (front-of-queue priority); student cancel closes as `Cancelled` and is only allowed before `Active`. `IsOpen` = `Pending | Active | AwaitingFaq | AwaitingConfirmation`.
     - **Queueing & Reservation:** Queue is ordered by `ReturnedToQueueAt DESC NULLS LAST`, then `QueuedAt ASC`, then `Id ASC`. Atomic teacher reservation uses PostgreSQL `SELECT ... FOR UPDATE SKIP LOCKED`. The queue is shared across all teachers joined to a session, while atomic request reservations remain bound to the individual claiming teacher. A partial unique index `IX_HelpRequest_OneOpenPerStudent` on `(SessionId, Generation, StudentId)` filtered by `Status IN ('Pending','Active','AwaitingFaq','AwaitingConfirmation')` enforces at most one open request per student per session generation.
     - **Queue Metrics:** Teacher session summaries differentiate between total waiting help requests and unreserved requests (`UnreservedWaitingCount`) to support multi-teacher load balancing. Cross-module waiting count queries aggregate generation-specific metrics using single-pass database groupings.
     - **Concurrency:** PostgreSQL `xmin` is used as the concurrency token on `HelpRequest`; message mutations update the aggregate root's timestamp to detect concurrent updates.
     - **Display Numbering:** A per-session/per-generation `HelpRequestNumberCounter` plus a PostgreSQL `BEFORE INSERT` trigger (`assign_request_number`) assigns sequential `RequestNumber` values. The computed `DisplayNumber` prefixes `BF-` for student requests and `FAQ-` for FAQ entries (e.g. `BF-001`, `FAQ-002`), resetting per generation.
     - **Contracts:** Exposes `GetWaitingCountsQuery`, `GetCompletedRequestStatisticsQuery`, `GetFaqAnswerRecipientsQuery`, `GetArchivedConversationsQuery`, `GetArchivedConversationCountsQuery`, `CanApplyGeneralizationQuery`, `ApplyGeneralizationCommand`. Cross-module authorization is centralized in the `HelpRequestAccess` helper (in-process `IsSessionParticipantQuery` / `IsSessionTeacherQuery` / `GetHelpRequestSessionQuery`).
     - **Endpoints — Student:**
       - `POST /api/students/sessions/{sessionId}/requests` (Create)
       - `GET /api/students/requests` (List own requests)
       - `GET /api/students/sessions/{sessionId}/queue` (Privacy-preserving queue status: position, waiting count, estimated wait time)
       - `GET /api/students/sessions/{sessionId}/queue/status` (Lightweight status poll)
       - `GET /api/students/requests/{id}` (Conversation details and messages)
       - `POST /api/students/requests/{id}/messages` (Follow-up message; reopens request in queue)
       - `PATCH /api/students/requests/{id}/status` (Cancel; Accept FAQ; Reject FAQ)
     - **Endpoints — Teacher:**
       - `GET /api/teachers/sessions/{sessionId}/queue` (Queue breakdown)
       - `GET /api/teachers/sessions/{sessionId}/queue/status` (Lightweight status poll)
       - `POST /api/teachers/sessions/{sessionId}/queue/take-next` (Atomic reservation)
       - `GET /api/teachers/requests/{id}` (Conversation details)
       - `POST /api/teachers/requests/{id}/messages` (Digital answer; completes request)
       - `PATCH /api/teachers/requests/{id}/status` (Transitions: `Start`, `Complete`, `Cancel`)
     - **Messaging:**
       - Consumes `requests / generalized.created` (`GeneralizedMessageSubscriber`) and `sessions / session.updated` (`SessionUpdatedMessageSubscriber`).
       - Publishes `HelpRequestUpdatedIntegrationEvent` (`requests / request.updated`) on all status and queue transitions, and `HelpRequestAnsweredMessage` (`requests / request.answered`) when digital answers are posted — both via transactional outbox.
  4. **KnowledgeBase Module (`knowledgebase` schema):**
     - **Aggregate:** `FaqAnswer` tracks generalized questions, their answers, and the set of source help request IDs, ensuring answers are recorded only once per generalization.
     - **Handlers:** `RegisterGeneralizationMessageHandler` validates and persists FAQ generalizations; `FaqAnsweredMessageHandler` records teacher answers to generalizations.
     - **Contracts:** Exposes `GetArchivedFaqsQuery`, `GetArchivedFaqCountsQuery` for in-process archive read-model assembly.
     - **Endpoints — Teacher:** `GET /api/teachers/sessions/{sessionId}/faqs` (List), `POST /api/teachers/sessions/{sessionId}/faqs` (Create).
     - **Endpoints — Student:** `GET /api/students/sessions/{sessionId}/faqs` (List).
     - **Migration history (Note):** The related backfills live in the **HelpRequests** module, not KnowledgeBase: `AddHelpRequestMessageType` (backfills `MessageType`), `KeepPhysicalHelpRequestsInQueue` (suppresses stale FAQ notices for physical requests from an earlier behavior), and `HideRejectedFaqMessages`. KnowledgeBase itself has only `InitialKnowledgeBase`.
  5. **Insights Module:**
     - Session statistics aggregator. `GetSessionStatisticsQuery` (with validator) backs `GET /api/teachers/sessions/{sessionId}/statistics`; it composes `GetCompletedRequestStatisticsQuery` from the HelpRequests module. Broader telemetry/reporting remains planned.

### 4.4 GnaService (Notification Microservice)
- **Role:** Asynchronous notification service (port 6001) with dedicated PostgreSQL database (`gna`).
- **Aggregate:** `Notification` (Id, `UserId`, `NotificationMessages`, `CreatedAt`, extended metadata: `SessionId`, `Generation`, `HelpRequestId`, `Change`, `Status`, `NotificationEventType`).
- **SignalR Hubs:** `/api/students/notifications` and `/api/teachers/notifications` push notifications to connected clients. Supports both student identity cookies and query-string JWT bearer tokens (`access_token`).
- **Delivery Pipeline:**
  - `NotificationMessageSubscriber` listens to exchange `notifications` with routing key `gna-notification-requested` (`NotificationCreatedMessage`) and persists notifications for recipient users.
  - `NotificationDispatcher` attempts delivery via active SignalR connections before falling back to push notification channels.
  - Delivery modes are explicit via `NotificationDeliveryType` (`RealtimeOrPush` vs `RealtimeOnly`), controlling whether fallback push channels are engaged.
  - Persistence is idempotent at the application level: `NotificationRepository` skips inserts whose `Id` already exists (`AnyAsync` check), so RabbitMQ redeliveries are safe. Only `push_subscriptions` has a DB unique constraint (`(UserId, Endpoint)`).
  - **Local outbox dispatch:** `NotificationCreatedDomainEvent` stages a `NotificationDeliveryRequestedMessage` (`notification.delivery-requested`) into the Gna outbox; a local `NotificationOutboxDispatcher` (500 ms poll) reads it inside `GnaService` — no internal RabbitMQ round-trip — and invokes `NotificationDispatcher`. A completed attempt is terminal, including "no channel available".
- **Real-time session events:** Session lifecycle transitions (clear/archive/delete/rename) enqueue `SessionChanged` notifications (`NotificationDeliveryType.RealtimeOnly`) for every session participant plus the session's `CreatedByTeacherId`, coalesced by `session-notification:{sessionId}:{userId}`. `NotificationEventType` (`FaqCreated`, `FaqAnswered`, etc.) drives context-specific titles and navigation targets on the frontend.
- **Persistence:** `PushSubscription` entities track active device subscriptions for fallback push delivery.

### 4.5 MimirService (Intelligence & Request Processing)
- **Role:** Async intelligence service (port 6002) with dedicated PostgreSQL database (`mimir`). Managed via Aspire AppHost.
- **Aggregates:** `HelpRequest` (Id, `Message`, `ReceivedAt`, `SessionId`, `Generation`, optional `GeneralizationId` — the link points from the request *to* the generalization); `Generalization` (Id, `SessionId`, `Generation`, `Question` (Danish FAQ string), `CreatedAt`) — carries no member collection.
- **Generalization pipeline:** `IGeneralizationDecider` decides `Standalone` / `Pair` / `Join` per incoming `HelpRequestCreatedMessage`. **The only active implementation is `PlaceholderGeneralizationDecider`** — it pairs only when another waiting request in the same session+generation has *identical* trimmed text (case-insensitive) and returns a fixed placeholder FAQ string (`"Dette er et generaliseret spørgsmål."`); real LLM-based decision is pending. A `Pair` instantiates a `Generalization`, links both requests, and emits `GeneralizedCreatedMessage` via the transactional outbox. `HelpRequestRepository` queries waiting, unlinked requests by `SessionId`+`Generation`. `ExistsAsync` guard makes redelivery idempotent.
- **LLM client (registered, not yet consumed):** `GeminiLlmClient` (typed `HttpClient` → `https://generativelanguage.googleapis.com/`, 60 s timeout, `x-goog-api-key`, `v1beta/models/{model}:generateContent`, resilience handlers removed). Iterates `Gemini:Models` with multi-model fallback (first non-empty wins, else `LlmUnavailableException`); skips `Part.Thought == true`. Config `Gemini`: `ApiKey`, `Models` (`gemini-3.5-flash-lite`, `gemini-3.1-flash-lite`, `gemini-3.5-flash`), `Temperature` 0.2. **Not injected into `HelpRequestMessageHandler`** — `PlaceholderGeneralizationDecider` still drives the flow.
- **Concurrency:** Optimistic concurrency via PostgreSQL `xmin` row token on `HelpRequest`.
- **Messaging consumers:** `HelpRequestMessageSubscriber` listens to `requests / request.created` (`HelpRequestCreatedMessage`); `SessionUpdatedMessageSubscriber` listens to `sessions / session.updated` (`SessionUpdatedIntegrationEvent`) to track session lifecycle. Subscriber queue `mimir-request-answered` is declared via `AddRabbitMqQueue`.
- **Evals:** `MimirService.Evals/generalization-cases.json` holds a Danish-language eval dataset for the decider (categories: `clear-match`, `clear-different`, `borderline`, `multi-candidate`, `typos`, `names`, `injection`, `empty-queue`), measuring decision accuracy and FAQ question quality. Harness is deferred to BF-10; **evals do not run in CI** because they hit the real model.
- **Dev/test endpoints (Development-only, anonymous):** `POST /api/test/generalizations` publishes a `GeneralizedCreatedMessage` directly; `POST /api/test/generalizations/from-help-requests` runs the generalization handler against supplied help request ids.

### 4.6 YggdrasilService (Aspire Orchestrator)
- **Role:** Distributed application host (`YggdrasilService.AppHost/AppHost.cs`) managing startup dependencies, container networking, and centralized service defaults (`YggdrasilService.ServiceDefaults`) for telemetry, logging, and health endpoints. Aspire 13.6.4 (`Aspire.AppHost.Sdk`).
- **Containers:** PostgreSQL on host port `5000` (init scripts under `./postgres/init`); RabbitMQ on `5001` with management plugin on `5002` (defaults `guest`/`guest` as secret parameters). All four databases (`eir`, `gna`, `mimir`, `var`) declared as Aspire database resources.
- **Service wiring:** `VarService` (6003), `EirService.Api` (6000), `GnaService.Api` (6001), `MimirService.Api` (6002, receives `Gemini__ApiKey` as a secret parameter), `HeimdallGateway` (6004 — waits on all three backends).
- **Frontends:** `glitnir-frontend` (`npm ci` + `start`, port 7000 → target 7002) and a secondary `test-frontend` (`Temp/TestFrontend`, port 7001 → target 7003), both waiting on `heimdallgateway`.

### 4.7 Shared Libraries (`src/Bifrost/Backend/Shared`)
- **`Shared.Domain`:** DDD base abstractions (`Entity<TId>`, `AggregateRoot<TId>`, `IAggregateRoot`, `IDomainEvent`, `DomainEvent`).
- **`Shared.Application`:** Messaging abstractions (`IMessageBus`, `MessageEnvelope<T>`, `MessageRoutes`), outbox interfaces (`OutgoingMessage`), UoW abstractions (`IUnitOfWork`, `IUnitOfWork<TContext>`, `UnitOfWorkConcurrencyException`), and `DomainEventNotification`.
- **`Shared.Infrastructure`:**
  - `RabbitMqMessageBus`: RabbitMQ client wrapper with OpenTelemetry distributed activity tracing.
  - `OutboxDispatcher<TDbContext>`: Background worker polling pending outbox messages and publishing to RabbitMQ with retry backoff and tracing propagation. Default polling interval is 5 s; `EirService.HelpRequests` (`AddOutbox<EirRequestDbContext>(500ms)`) and `GnaService`'s `NotificationOutboxDispatcher` are tuned to 500 ms to minimize integration event propagation latency.
  - `UnitOfWork<TOwner, TDbContext>`: EF Core Unit of Work that discovers domain events on tracked aggregates, triggers domain event handlers, and commits aggregate mutations alongside staged `OutboxMessage` records atomically.
  - `RabbitMqQueueInitializer`: Hosted service (`AddRabbitMqQueue`) that pre-declares durable queues ahead of their consumers ("prepare a durable destination while its message handler is still being developed"). Used for: `mimir-session-updated` (declared by Sessions), `gna-notification-requested` + `gna-request-updated` (Gna), `mimir-request-answered` (Mimir). Active subscribers declare their own queues via `SubscribeAsync`: `mimir-request-created` + `mimir-session-updated` (Mimir), `gna-notification-requested` (Gna), `eir-helprequests-session-updated` (Eir HelpRequests), `eir-faq-answered` (Eir KnowledgeBase).
  - Database migration utilities (`DatabaseMigrationExtensions.MigrateAtStartup()`).
- **`Shared.Api`:**
  - `StudentCookieHandler` & `StudentIdentityCookie`: ASP.NET Data Protection cookie scheme for student sessions.
  - `AuthenticationServiceCollectionExtensions`: Centralized configuration for dual JWT Bearer / Student Cookie authentication policies.

---

## 5. Messaging & Integration Event Catalog

| Exchange | Routing Key | Message Contract | Publisher | Consumers |
| :--- | :--- | :--- | :--- | :--- |
| `sessions` | `session.updated` | `SessionUpdatedIntegrationEvent`<br>*(EventId, SessionId, Generation, EventType: Cleared\|Deleted\|Archived, UpdatedAtUtc, PreviousGeneration?)* | `EirService.Sessions` (Outbox) | `EirService.HelpRequests` (queue `eir-helprequests-session-updated`), `MimirService` (queue `mimir-session-updated`) |
| `requests` | `request.created` | `HelpRequestCreatedMessage`<br>*(HelpRequestId, SessionId, Generation, Message)* | `EirService.HelpRequests` (Outbox) | `MimirService` (queue `mimir-request-created`) |
| `requests` | `request.updated` | `HelpRequestUpdatedIntegrationEvent`<br>*(EventId, HelpRequestId, SessionId, Generation, StudentId?, Change, Status, HelpType, AssignedTeacherId?, QueuedAt, OccurredAt)* | `EirService.HelpRequests` (Outbox) | **No active consumer** (queue `gna-request-updated` pre-declared only) |
| `requests` | `request.answered` | `HelpRequestAnsweredMessage`<br>*(HelpRequestId, SessionId, Generation, Question, Answer)* | `EirService.HelpRequests` (Outbox) | **No active consumer** (queue `mimir-request-answered` pre-declared only) |
| `requests` | `generalized.created` | `GeneralizedCreatedMessage`<br>*(GeneralizationId, SessionId, Generation, Question, HelpRequestIds[])* | `MimirService` (Outbox) | `EirService.KnowledgeBase` (`RegisterGeneralizationMessageSubscriber`) |
| `requests` | `faq.answered` | `FaqAnsweredMessage`<br>*(FaqId, SessionId, Generation, Answer, TeacherId, AnsweredAt, Question?)* | `EirService.HelpRequests` (Outbox) | `EirService.KnowledgeBase` (queue `eir-faq-answered`) |
| `notifications` | `gna-notification-requested` | `NotificationCreatedMessage`<br>*(UserId, Messages, SessionId?, Generation?, HelpRequestId?, Change?, Status?, EventType, DeliveryType)* | Eir (`Sessions`/`HelpRequests`/`KnowledgeBase`) | `GnaService` (queue `gna-notification-requested`) |
| `notifications` | `notification.delivery-requested` | `NotificationDeliveryRequestedMessage`<br>*(NotificationId, UserId, Message, CreatedAt, SessionId?, HelpRequestId?, Generation?, Change?, Status?, EventType, DeliveryType)* | `GnaService` (internal outbox) | `GnaService` `NotificationOutboxDispatcher` (local, no broker) |

**Coalescing:** Outgoing messages staged within a single `UnitOfWork` commit may be coalesced by key, allowing personal notifications to supersede generic updates while still preserving at-least-once delivery semantics.

---

## 6. ReviewBot Review Guidelines & Checklist

When evaluating Pull Requests, the AI Reviewer must verify:
1. **Clean Architecture Adherence:** No leaking of infrastructure concepts (EF Core, SQL, HTTP contexts) into Application or Domain layers.
2. **Modular Monolith Isolation in EirService:** No cross-module database contexts, navigation properties, or cross-project dependencies between module internals.
3. **Transactional Outbox Usage:** All integration events must be enqueued via `UnitOfWork` / domain event notification rather than published synchronously to RabbitMQ.
4. **FastEndpoints Pattern:** HTTP endpoints must follow the REPR pattern and validate inputs using FluentValidation classes rather than inline controller logic.
5. **Concurrency & Generations:** Changes affecting session lifecycle must handle `CurrentGeneration` concurrency tokens correctly.
6. **English in Codebase (developer-facing):** All code identifiers, comments, log messages, exception messages, and documentation must be in English (applies to backend C#, frontend TypeScript, and shell scripts). **User-facing UI text and test data are exempt** and may be Danish, consistent with the rest of the application.
7. **Frontend:** Verify against §3.3 (feature isolation via `index.ts` barrels, `@angular/forms/signals`, `bifrost-` selectors, no legacy Angular patterns, Vitest-only, cookie-auth with no `localStorage`/`sessionStorage` tokens). English codebase rule applies to TypeScript as well; user-facing UI text and test data may be Danish.
