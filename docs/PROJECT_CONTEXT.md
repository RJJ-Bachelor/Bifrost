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
- **Backend Runtime & Language:** .NET 10 (`net10.0`), C# 13.
- **Orchestration & Diagnostics:** .NET Aspire (`YggdrasilService.AppHost` & `YggdrasilService.ServiceDefaults`) orchestrating PostgreSQL databases, RabbitMQ broker, OpenTelemetry metrics, traces, and health checks.
- **API Framework:** FastEndpoints 8.3.0 (REPR pattern: Request-Endpoint-Response) for API endpoints, internal in-process commands, and domain event notifications. Global serializer uses `JsonStringEnumConverter` for string-based enum serialization.
- **Validation:** FluentValidation 12.1.1 integrated into FastEndpoints via pipeline command middleware (`CommandValidationMiddleware`).
- **Gateway & Reverse Proxy:** YARP (Yet Another Reverse Proxy) in `HeimdallGateway` managing edge routing, token extraction, and student cookie issuance.
- **Identity & Authentication:**
  - **Teachers:** Duende IdentityServer (in `VarService`) with ASP.NET Core Identity for OAuth 2.0 / OpenID Connect authorization code flow with PKCE and JWT Bearer tokens.
  - **Students:** ASP.NET Core Data Protection encrypted cookie authentication (`bifrost-anonymous-user`) issuing anonymous student identifiers.
- **Databases & Persistence:**
  - PostgreSQL 16+ via Entity Framework Core 10 (`10.0.12`) and Npgsql.
  - Isolated database per service (`eir`, `gna`, `mimir`, `var`).
  - Isolated database schema per module in `EirService` (`sessions`, `participants`, `helprequests`, `knowledgebase`).
- **Message Broker & Event-Driven Architecture:**
  - RabbitMQ 4+ (`RabbitMQ.Client 7.1.2`).
  - Custom abstraction `IMessageBus` (`RabbitMqMessageBus`) with W3C distributed trace context propagation via OpenTelemetry `ActivitySource`.
  - Transactional Outbox Pattern (`OutboxDispatcher`, `OutboxMessage`, `OutgoingMessage`) for reliable at-least-once message dispatching.
  - Unit of Work pattern (`IUnitOfWork`, `IUnitOfWork<TContext>`) guaranteeing atomic persistence of aggregate changes and outbox messages.
  - Outbox message coalescing (`OutgoingMessage.Coalesce`) for keyed deduplication of messages within a single database commit, prioritizing personal notifications over generic updates.
- **Real-time Notifications:** SignalR hubs (`/api/students/notifications`, `/api/teachers/notifications`) in `GnaService`, supporting both student identity cookies and query-string JWT bearer tokens (`access_token`).
- **Frontend:**
  - Angular 22 (`glitnir-frontend`), standalone components, signals, zoneless change detection, Vitest.
  - Feature-based structure (`core/`, `features/`, `shared/`) with role-split routing (`/` for students, `/teacher/*` for teachers).
  - Cookie-based OAuth via HeimdallGateway (`bifrost-teacher`) and anonymous student identity cookies (`bifrost-anonymous-user`); `HttpClient` / `httpResource` configured with `withCredentials: true`.
  - Component selectors standardized with the `bifrost-` prefix across all modules.
  - Role-based visual themes applied via host class bindings (`.theme-teacher`) driven by the global `AuthStore`; favicon switches automatically based on role.
  - Integrated into Aspire orchestration via `.esproj` and `AddJavaScriptApp` in AppHost.
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

The Angular application (`src/Bifrost/Frontend/glitnir-frontend`) follows a feature-based architecture:

- **`core/`** — global singletons (auth, layout, guards). No feature knowledge.
- **`shared/`** — reusable UI-only components with no domain knowledge (`Card`, `Dialog`, `ListRow`, `StatusDialog`, `StatTile`).
- **`features/`** — business domains (`session`, `faq`, `student`, `teacher`, `not-found`), each isolated with its own services, components and models.

**Rules:**

- Features are isolated. Cross-feature imports happen only via public APIs.
- One feature never imports from another feature's internals.
- `core/` services and stores are exposed via `@Service()` (Angular v22 shorthand).
- Models use `.model.ts` suffix (Angular official exception to the "no type suffixes" rule).
- Role-based access is enforced with `CanMatchFn` guards (`studentGuard`, `teacherGuard`) that short-circuit lazy loading.
- Topbar layout is included per page (not global in `app.html`) and supports content projection through named slots.
- State management relies on Angular signals, service singletons, and `httpResource` / `HttpClient` for cookie-backed API calls.
- Global layout utilities (`surface-box`, `icon-mask`, `input-field`, `form-error`) live in `styles/_utilities.scss` and are composed via class lists rather than duplicated per component.
- Real-time view updates use bitmasked `SessionRefresh` flags so SignalR events trigger only the necessary reloads (session, queue, FAQ, statistics) rather than a full-page refresh.

**Teacher session view (`features/teacher/pages/session`):**

- `TeacherSession` is a smart/container component orchestrating queue reservations (`takeNext`), archiving, statistics tiles, and FAQ CRUD.
- Modal flows are split into dedicated presentational components:
  - `SessionSettingsDialog` — session codes, rename (placeholder until BF-47), delete trigger
  - `SessionDeleteDialog` — destructive confirmation, calls delete API
  - `ArchiveConfirmDialog` — archive title form, calls archive API
- FAQ management lives in `features/faq/components`:
  - `FaqFormDialog` — create/edit via discriminated `FaqDialogState` (`closed` | `create` | `edit`)
  - `FaqDeleteDialog` — delete confirmation
- Dialog state that spans multiple dialogs (settings → delete) is held by the parent; dialog-local state uses `linkedSignal` for automatic reset when the source (open state or target item) changes.

**Student session view (`features/student/pages/session`):**

- `StudentSession` renders queue status, question history, FAQ archive list, and message form.
- Session generation history is exposed via `FaqArchiveList` with a dynamic `maxItems` count.

**Testing:**

- All services and non-trivial business logic must have unit tests. Pure shell components (layout-only) are exempt.
- Dialog components with real logic (`FaqFormDialog`, `ArchiveConfirmDialog`, `SessionSettingsDialog`, `FaqDeleteDialog`, `SessionDeleteDialog`) are tested in isolation via Vitest + Angular TestBed.
- jsdom does not implement `<dialog>.showModal()` / `.close()`; tests stub these on `HTMLDialogElement.prototype` in `beforeAll`.

---

## 4. System Services & Modules Breakdown

### 4.1 HeimdallGateway (Edge API Gateway)
- **Role:** Central entry point for external traffic (port 6004). Routes incoming traffic via YARP to backend services.
- **Authentication Handling:**
  - `/api/students/**`: Requires `Student` authorization policy. The `StudentIdentityMiddleware` automatically intercepts requests missing the `bifrost-student` cookie, mints an encrypted identity cookie, and forwards the authenticated request downstream.
  - `/api/teachers/**`: Requires `Teacher` authorization policy. Accepts Bearer JWT tokens or reads the `bifrost-teacher` cookie (OIDC session) and attaches the access token as a `Bearer` header to downstream requests.
  - `/api/monitoring/**`: Anonymous access pass-through to backend health/monitoring endpoints.
  - `/account/login` and `/account/logout`: OIDC login/logout challenge endpoints interacting with `VarService`.

### 4.2 VarService (Identity & Access Management)
- **Role:** Dedicated Duende IdentityServer instance (port 6003) backed by ASP.NET Core Identity and PostgreSQL (`var` database).
- **Clients & Scopes:**
  - `interactive`: Code flow with PKCE for teacher web interfaces (scopes: `openid`, `profile`, `roles`, `Bifrost`).
  - `m2m.client`: Machine-to-machine client credentials (scopes: `Bifrost`).
- **Seed Users:**
  - Teachers: `knud`, `bob` (Role: `Teacher`).
  - Students (dev only): `learner` (Role: `Student`).

### 4.3 EirService (Core Modular Monolith)
- **Role:** Central domain service (port 6000) managing classroom sessions, participants, help requests, and the knowledge base.
- **Modules:**
  1. **Sessions Module (`sessions` schema):**
     - **Aggregate:** `Session` (Title, 8-char Crockford Base32 `SessionCode`, distinct `TeacherCode`, `CreatedByTeacherId`, `CurrentGeneration`, `LastClearedAtUtc`).
     - **Domain Events:** `SessionClearedDomainEvent`, `SessionDeletedDomainEvent`, `SessionArchivedDomainEvent`.
     - **Integration Promotion:** Domain events promote to `SessionUpdatedIntegrationEvent` (`Cleared` | `Deleted` | `Archived`) and stage them into the `sessions` outbox.
     - **Lifecycle:** Clearing bumps generation and resets queue/FAQ; archiving snapshots the current generation (JSONB conversation snapshot + knowledge base read model) before advancing the generation; deletion removes the session and broadcasts to participants and teachers. Role-restricted archive inspection endpoints enforce that students access only their own conversations and answered FAQs.
     - **Concurrency:** Generation-based concurrency validation on archive execution guarantees strict atomic lifecycle transitions under concurrent requests.
     - **Endpoints — Teacher:**
       - `POST /api/teachers/sessions` (Create)
       - `GET /api/teachers/sessions` (`GetSessions` — includes live `WaitingCount` per generation, excluding active/confirmation-waiting items)
       - `GET /api/teachers/sessions/{id}` (Details)
       - `POST /api/teachers/sessions/{id}/clear` (Clear / bump generation)
       - `POST /api/teachers/sessions/{id}/archive` (`ArchiveSession`; `ArchiveSessionCommand` accepts an optional title)
       - `POST /api/teachers/sessions/join` (Join by `TeacherCode`)
       - `DELETE /api/teachers/sessions/{id}` (Delete)
       - `GET /api/teachers/sessions/{id}/archives` (List archives)
     - **Endpoints — Student:**
       - `GET /api/students/sessions/{code}` (Lookup session summary by code)
       - `GET /api/students/sessions/{id}/archives` (Own archive view)
     - **Contracts:** Exposes `FindSessionByCodeQuery`, `GetSessionGenerationsQuery`, `GetSessionSummariesQuery`, `GetSessionTeacherIdsQuery`, `GetTeacherSessionIdsQuery`, `IsSessionTeacherQuery` for in-process module consumption.
  2. **Participants Module (`participants` schema):**
     - **Entity:** `SessionParticipant` (Composite key: `SessionId`, `StudentSubjectId`, `JoinedAtUtc`) with explicit `ParticipantRole` (`Student` | `Teacher`) for fine-grained permission boundaries.
     - **Endpoints:**
       - Student: `POST /api/students/sessions/join` (Join session by code), `GET /api/students/sessions` (List joined sessions), `GET /api/students/me` (Student identity).
       - Teacher: `GET /api/teachers/me` (Teacher identity).
     - **Contracts:** Exposes `IsSessionParticipantQuery` and `IsSessionTeacherQuery` to verify enrollment and teacher access.
  3. **HelpRequests Module (`helprequests` schema):**
     - **Aggregate:** `HelpRequest` is a conversation thread containing child `HelpRequestMessage` entities ordered by `SequenceNumber`. Fields include `UserId`, `SessionId`, `Generation`, `HelpType` (`Physical` | `Digital`), `HelpRequestStatus`, `FaqAnswerId`, `HelpRequestMessageType` (`Original` | `FollowUp` | `SystemFaq`), and a per-generation `RequestNumber`.
     - **Status Lifecycle:** `Pending` → `Active` → `AwaitingConfirmation` → `Completed`. FAQ-linked requests enter `AwaitingFaq` while a generalization awaits student confirmation. Students can append follow-up messages to reopen `Completed` requests back into `Pending`. Teacher cancellation returns a request to `Pending` with front-of-queue priority; student cancellation closes it as `Cancelled`.
     - **Queueing & Reservation:** Queue is ordered by `ReturnedToQueueAt DESC NULLS LAST`, then `QueuedAt ASC`, then `Id ASC`. Atomic teacher reservation uses PostgreSQL `SELECT ... FOR UPDATE SKIP LOCKED`. The queue is shared across all teachers joined to a session, while atomic request reservations remain bound to the individual claiming teacher. A partial unique index enforces at most one open request (`Pending` | `Active` | `AwaitingConfirmation`) per student per session generation.
     - **Queue Metrics:** Teacher session summaries differentiate between total waiting help requests and unreserved requests (`UnreservedWaitingCount`) to support multi-teacher load balancing. Cross-module waiting count queries aggregate generation-specific metrics using single-pass database groupings.
     - **Concurrency:** PostgreSQL `xmin` is used as the concurrency token on `HelpRequest`; message mutations update the aggregate root's timestamp to detect concurrent updates.
     - **Display Numbering:** A per-session/per-generation `HelpRequestNumberCounter` with a PostgreSQL `BEFORE INSERT` trigger assigns sequential `DisplayNumber` values (`BF-001`, `BF-002`, ...), resetting per generation.
     - **Contracts:** Exposes `GetHelpRequestSessionQuery` (verifies session ownership, generation, and teacher identity) alongside `IsSessionParticipantQuery`. The `HelpRequestAccess` helper centralizes cross-module authorization checks.
     - **Endpoints — Student:**
       - `POST /api/students/sessions/{sessionId}/requests` (Create)
       - `GET /api/students/sessions/{sessionId}/queue` (Privacy-preserving queue status: position, waiting count, estimated wait time)
       - `GET /api/students/requests/{id}` (Conversation details and messages)
       - `POST /api/students/requests/{id}/messages` (Follow-up message; reopens request in queue)
       - `PATCH /api/students/requests/{id}/status` (Cancel; Accept FAQ; Reject FAQ)
     - **Endpoints — Teacher:**
       - `GET /api/teachers/sessions/{sessionId}/queue` (Queue breakdown)
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
     - **Migration:** Backfills `MessageType` for existing help request messages and suppresses redundant notifications for physical help requests previously affected by FAQ state changes.
  5. **Insights Module:**
     - Scaffolded bounded context for session metrics, telemetry analysis, and reporting.

### 4.4 GnaService (Notification Microservice)
- **Role:** Asynchronous notification service (port 6001) with dedicated PostgreSQL database (`gna`).
- **Aggregate:** `Notification` (Id, `UserId`, `NotificationMessages`, `CreatedAt`, extended metadata: `SessionId`, `Generation`, `HelpRequestId`, `Change`, `Status`, `NotificationEventType`).
- **SignalR Hubs:** `/api/students/notifications` and `/api/teachers/notifications` push notifications to connected clients. Supports both student identity cookies and query-string JWT bearer tokens (`access_token`).
- **Delivery Pipeline:**
  - `NotificationMessageSubscriber` listens to exchange `notifications` with routing key `gna-notification-requested` (`NotificationCreatedMessage`) and persists notifications for recipient users.
  - `NotificationDispatcher` attempts delivery via active SignalR connections before falling back to push notification channels.
  - Delivery modes are explicit via `NotificationDeliveryType` (`RealtimeOrPush` vs `RealtimeOnly`), controlling whether fallback push channels are engaged.
  - Notification persistence enforces idempotency using unique constraints and transaction handling to safely handle redelivered messages.
- **Real-time Session Events:** Session lifecycle transitions (clearing, archiving, deletion) broadcast `SessionChanged` events to all session participants and associated teachers, and `NotificationEventType` (`FaqCreated`, `FaqAnswered`, etc.) drives context-specific titles and navigation targets on the frontend.
- **Local Outbox Dispatch:** A local `NotificationOutboxDispatcher` worker reads notification outbox records directly within `GnaService`, eliminating internal RabbitMQ round-trips. It marks terminal states and applies idempotency protections to ensure reliable dispatch over active SignalR connections. Notification infrastructure separates broker-bound integration event handling from local outbox-driven delivery.
- **Persistence:** `PushSubscription` entities track active device subscriptions for fallback push delivery.

### 4.5 MimirService (Intelligence & Request Processing)
- **Role:** Asynchronous intelligence and request analysis service (port 6002) with dedicated PostgreSQL database (`mimir`). Integrates with the Google Gemini LLM API via `ILlmClient` and `GeminiLlmClient` in the infrastructure layer to process student queries. Managed via Aspire AppHost.
- **Aggregate:** `HelpRequest` (Id, `Message`, `ReceivedAt`, `SessionId`, `Generation`).
- **Idempotency & Resilience:** Message handlers verify request existence before processing, avoiding redundant LLM calls on message redeliveries. `LlmUnavailableException` fallback gracefully forwards the original student query if all configured LLM models fail.
- **Messaging Consumers:**
  - `HelpRequestMessageSubscriber`: Listens to exchange `requests` with routing key `request.created` (`HelpRequestCreatedMessage` mandating `HelpRequestId`, `SessionId`, `Generation`). Stores the request and emits a `GeneralizedCreatedMessage` to `requests / generalized.created` via its transactional outbox.
  - `SessionUpdatedMessageSubscriber`: Listens to exchange `sessions` with routing key `session.updated` (`SessionUpdatedIntegrationEvent`) to track session lifecycle transitions (`Cleared`, `Deleted`, `Archived`).

### 4.6 YggdrasilService (Aspire Orchestrator)
- **Role:** Distributed application host (`YggdrasilService.AppHost`) managing startup dependencies, container networking for PostgreSQL and RabbitMQ, and centralized service defaults (`YggdrasilService.ServiceDefaults`) for telemetry, logging, and health endpoints.

### 4.7 Shared Libraries (`src/Bifrost/Backend/Shared`)
- **`Shared.Domain`:** DDD base abstractions (`Entity<TId>`, `AggregateRoot<TId>`, `IAggregateRoot`, `IDomainEvent`, `DomainEvent`).
- **`Shared.Application`:** Messaging abstractions (`IMessageBus`, `MessageEnvelope<T>`, `MessageRoutes`), Outbox interfaces (`OutgoingMessage` with `Coalesce` support), and Unit of Work abstractions (`IUnitOfWork`, `IUnitOfWork<TContext>`, `UnitOfWorkConcurrencyException`).
- **`Shared.Infrastructure`:**
  - `RabbitMqMessageBus`: RabbitMQ client wrapper with OpenTelemetry distributed activity tracing.
  - `OutboxDispatcher<TDbContext>`: Background worker polling pending outbox messages and publishing to RabbitMQ with retry backoff and tracing propagation. Polling intervals for `EirService.HelpRequests` and `GnaService` are tuned to 500ms to minimize integration event propagation latency.
  - `UnitOfWork<TOwner, TDbContext>`: EF Core Unit of Work that discovers domain events on tracked aggregates, triggers domain event handlers, and commits aggregate mutations alongside staged `OutboxMessage` records atomically.
  - `RabbitMqQueueInitializer`: Hosted service (`AddRabbitMqQueue`) that declares durable subscriber queues ahead of active consumers (e.g. `gna-request-updated` in `GnaService`, `mimir-request-answered` in `MimirService`).
  - Database migration utilities (`DatabaseMigrationExtensions.MigrateAtStartup()`).
- **`Shared.Api`:**
  - `StudentCookieHandler` & `StudentIdentityCookie`: ASP.NET Data Protection cookie scheme for student sessions.
  - `AuthenticationServiceCollectionExtensions`: Centralized configuration for dual JWT Bearer / Student Cookie authentication policies.

---

## 5. Messaging & Integration Event Catalog

| Exchange | Routing Key | Message Contract | Publisher | Consumers |
| :--- | :--- | :--- | :--- | :--- |
| `sessions` | `session.updated` | `SessionUpdatedIntegrationEvent`<br>*(SessionId, Generation, EventType: Cleared\|Deleted\|Archived, PreviousGeneration)* | `EirService.Sessions` (Outbox) | `EirService.HelpRequests`, `MimirService` |
| `requests` | `request.created` | `HelpRequestCreatedMessage`<br>*(HelpRequestId, Message, SessionId, Generation)* | `EirService.HelpRequests` (Outbox) | `MimirService` |
| `requests` | `request.updated` | `HelpRequestUpdatedIntegrationEvent`<br>*(RequestId, SessionId, Generation, Status)* | `EirService.HelpRequests` (Outbox) | `GnaService` |
| `requests` | `request.answered` | `HelpRequestAnsweredMessage`<br>*(RequestId, SessionId, Generation, Message)* | `EirService.HelpRequests` (Outbox) | `MimirService` |
| `requests` | `generalized.created` | `GeneralizedCreatedMessage`<br>*(HelpRequestId, Message, SessionId, Generation)* | `MimirService` (Outbox) | `EirService.HelpRequests` |
| `notifications` | `gna-notification-requested` | `NotificationCreatedMessage`<br>*(UserId, Messages, SessionId, Generation)* | External / Domain Triggers | `GnaService` |

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
7. **Frontend Architecture & Testing:**
   - Feature isolation: no cross-feature imports of internal code.
   - No legacy Angular patterns (`NgModule`, constructor injection, `*ngIf`, Karma).
   - Unit tests required for all services and business logic; skip tests for pure shell components.
   - Cookie-based auth only — no tokens in `localStorage` or `sessionStorage`.
   - English codebase rule applies to TypeScript as well.

---

## Update 2026-10-28

- The Angular frontend fully implements the teacher session view (`TeacherSession`), orchestrating queue reservations, live wait-time tracking, and real-time statistics display.
- Teacher FAQ management is supported through dedicated modal dialogs (`FaqFormDialog`, `FaqDeleteDialog`) for creating, updating, and removing session-scoped knowledge base entries.
- Session administration features dedicated modal flows for viewing access codes, renaming, archiving generations, and destructive session deletion.
- The Angular application dynamically switches browser favicons based on user role driven by the global `AuthStore`.
- Frontend components adopt standardized global utility classes (`.icon-mask`, `.input-field`, `.surface-box`) and button variants across feature modules.
