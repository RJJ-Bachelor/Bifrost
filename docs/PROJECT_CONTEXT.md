# Project Context: Bifrost (Used and updated by the ReviewBot)

## 1. Overview & Purpose
Bifrost is a classroom communication and interaction platform designed to facilitate real-time engagement between teachers and students. Developed as a software engineering bachelor's project by Jonas, Jakob, and Rune, the system is architected around Clean Architecture, Domain-Driven Design (DDD), and event-driven microservice/modular-monolith patterns to ensure high scalability, loose coupling, and clear domain boundaries.

Key capabilities:
- **Session Management:** Teachers create, monitor, clear (incrementing generation), and delete interactive sessions identified by unique 8-character codes.
- **Student Participation:** Students join active sessions anonymously using cryptographically signed session cookies, without requiring permanent user accounts.
- **Help Requests:** Students submit help requests tied to active sessions and specific session generations, which are processed, tracked, and answered.
- **Asynchronous Intelligence & Notifications:** Distributed background services handle AI request generalization, real-time message routing, notifications, and analytics.

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
  - Isolated database schema per module in `EirService` (`sessions`, `participants`, `helprequests`).
- **Message Broker & Event-Driven Architecture:**
  - RabbitMQ 4+ (`RabbitMQ.Client 7.1.2`).
  - Custom abstraction `IMessageBus` (`RabbitMqMessageBus`) with W3C distributed trace context propagation via OpenTelemetry `ActivitySource`.
  - Transactional Outbox Pattern (`OutboxDispatcher`, `OutboxMessage`, `OutgoingMessage`) for reliable at-least-once message dispatching.
  - Unit of Work pattern (`IUnitOfWork`, `IUnitOfWork<TContext>`) guaranteeing atomic persistence of aggregate changes and outbox messages.
- **Frontend:**
  - Angular 22 (`glitnir-frontend`), standalone components, signals, zoneless, Vitest.
  - Feature-based structure (`core/`, `features/`, `shared/`) with role-split routing (`/` for students, `/teacher/*` for teachers).
  - Cookie-based OAuth via HeimdallGateway (`bifrost-teacher`) and anonymous student identity cookies (`bifrost-anonymous-user`).
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
   - All code, symbols, class/method/variable names, log messages, exception messages, and code comments must be written **100% in English**.

### 3.3 Frontend Architecture

The Angular application (`src/Bifrost/Frontend/glitnir-frontend`) follows a feature-based architecture:

- **`core/`** — global singletons (auth, layout, guards). No feature knowledge.
- **`shared/`** — reusable UI-only components with no domain knowledge.
- **`features/`** — business domains (`session`, `faq`, `student`, `teacher`, `not-found`), each isolated with its own services, components and models.

**Rules:**

- Features are isolated. Cross-feature imports happen only via public APIs.
- One feature never imports from another feature's internals.
- `core/` services and stores are exposed via `@Service()` (Angular v22 shorthand).
- Models use `.model.ts` suffix (Angular official exception to the "no type suffixes" rule).
- Role-based access is enforced with `CanMatchFn` guards (`studentGuard`, `teacherGuard`) that short-circuit lazy loading.
- Topbar layout is included per page (not global in `app.html`) and supports content projection through named slots.

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
- **Role:** Central domain service (port 6000) managing classroom sessions, participants, and help requests.
- **Modules:**
  1. **Sessions Module (`sessions` schema):**
     - **Aggregate:** `Session` (Title, 8-char Crockford Base32 `SessionCode`, `CreatedByTeacherId`, `CurrentGeneration`, `LastClearedAtUtc`).
     - **Domain Events:** `SessionClearedDomainEvent`, `SessionDeletedDomainEvent`.
     - **Integration Promotion:** `SessionClearedIntegrationEventUpgradeHandler` and `SessionDeletedIntegrationEventUpgradeHandler` map domain events to `SessionUpdatedIntegrationEvent` and stage them into the `sessions` outbox.
     - **Endpoints:**
       - Teacher: `POST /api/teachers/sessions` (Create), `GET /api/teachers/sessions` (List), `GET /api/teachers/sessions/{id}` (Details), `POST /api/teachers/sessions/{id}/clear` (Clear / bump generation), `DELETE /api/teachers/sessions/{id}` (Delete).
       - Student: `GET /api/students/sessions/{code}` (Lookup session summary by code).
     - **Contracts:** Exposes `FindSessionByCodeQuery`, `GetSessionGenerationsQuery`, and `GetSessionSummariesQuery` for in-process module consumption.
  2. **Participants Module (`participants` schema):**
     - **Entity:** `SessionParticipant` (Composite key: `SessionId`, `StudentSubjectId`, `JoinedAtUtc`).
     - **Endpoints:**
       - Student: `POST /api/students/sessions/join` (Join session by code), `GET /api/students/sessions` (List joined sessions), `GET /api/students/me` (Student identity).
       - Teacher: `GET /api/teachers/me` (Teacher identity).
     - **Contracts:** Exposes `IsSessionParticipantQuery` to verify student enrollment.
  3. **HelpRequests Module (`helprequests` schema):**
     - **Aggregate:** `HelpRequest` is a conversation thread containing child `HelpRequestMessage` entities ordered by `SequenceNumber`. Fields include `UserId`, `SessionId`, `Generation`, `HelpType` (`Physical` | `Digital`), `HelpRequestStatus`, and a per-generation `RequestNumber`.
     - **Status Lifecycle:** `Pending` → `Active` → `AwaitingConfirmation` → `Completed`. Students can append follow-up messages to reopen `Completed` requests back into `Pending`. Teacher cancellation returns a request to `Pending` with front-of-queue priority; student cancellation closes it as `Cancelled`.
     - **Queueing & Reservation:** Queue is ordered by `ReturnedToQueueAt DESC NULLS LAST`, then `QueuedAt ASC`, then `Id ASC`. Atomic teacher reservation uses PostgreSQL `SELECT ... FOR UPDATE SKIP LOCKED`. A partial unique index enforces at most one open request (`Pending` | `Active` | `AwaitingConfirmation`) per student per session generation.
     - **Concurrency:** PostgreSQL `xmin` is used as the concurrency token on `HelpRequest`; message mutations update the aggregate root's timestamp to detect concurrent updates.
     - **Display Numbering:** A per-session/per-generation `HelpRequestNumberCounter` with a PostgreSQL `BEFORE INSERT` trigger assigns sequential `DisplayNumber` values (`BF-001`, `BF-002`, ...), resetting per generation.
     - **Contracts:** Exposes `GetHelpRequestSessionQuery` (verifies session ownership, generation, and teacher identity) alongside `IsSessionParticipantQuery`. The `HelpRequestAccess` helper centralizes cross-module authorization checks.
     - **Endpoints — Student:**
       - `POST /api/students/sessions/{sessionId}/requests` (Create; replaces legacy `/api/students/createhelprequest`)
       - `GET /api/students/sessions/{sessionId}/queue` (Privacy-preserving queue status: position, waiting count, estimated wait time)
       - `GET /api/students/requests/{id}` (Conversation details and messages)
       - `POST /api/students/requests/{id}/messages` (Follow-up message; reopens request in queue)
       - `PATCH /api/students/requests/{id}/status` (Cancel)
     - **Endpoints — Teacher:**
       - `GET /api/teachers/sessions/{sessionId}/queue` (Queue breakdown)
       - `POST /api/teachers/sessions/{sessionId}/queue/take-next` (Atomic reservation)
       - `GET /api/teachers/requests/{id}` (Conversation details)
       - `POST /api/teachers/requests/{id}/messages` (Digital answer; completes request)
       - `PATCH /api/teachers/requests/{id}/status` (Transitions: `Start`, `Complete`, `Cancel`)
     - **Messaging:**
       - Consumes `requests / generalized.created` (`GeneralizedMessageSubscriber`) and `sessions / session.updated` (`SessionUpdatedMessageSubscriber`).
       - Publishes `HelpRequestUpdatedIntegrationEvent` (`requests / request.updated`) on all status and queue transitions, and `HelpRequestAnsweredMessage` (`requests / request.answered`) when digital answers are posted — both via transactional outbox.
  4. **KnowledgeBase Module:**
     - Scaffolded bounded context for domain questions, answers, and educational knowledge base.
  5. **Insights Module:**
     - Scaffolded bounded context for session metrics, telemetry analysis, and reporting.

### 4.4 GnaService (Notification Microservice)
- **Role:** Asynchronous notification service (port 6001) with dedicated PostgreSQL database (`gna`).
- **Aggregate:** `Notification` (Id, `UserId`, `NotificationMessages`, `CreatedAt`).
- **Messaging Consumer:** `NotificationMessageSubscriber` listens to exchange `notifications` with routing key `gna-notification-requested` (`NotificationCreatedMessage`) and persists notifications for recipient users.

### 4.5 MimirService (Intelligence & Request Processing)
- **Role:** Asynchronous intelligence and request analysis service (port 6002) with dedicated PostgreSQL database (`mimir`). Integrates with the Google Gemini LLM API via `ILlmClient` and `GeminiLlmClient` in the infrastructure layer to process student queries. Managed via Aspire AppHost.
- **Aggregate:** `HelpRequest` (Id, `Message`, `ReceivedAt`).
- **Messaging Consumers:**
  - `HelpRequestMessageSubscriber`: Listens to exchange `requests` with routing key `request.created` (`HelpRequestCreatedMessage`). Stores the request and emits a `GeneralizedCreatedMessage` to `requests / generalized.created` via its transactional outbox.
  - `SessionUpdatedMessageSubscriber`: Listens to exchange `sessions` with routing key `session.updated` (`SessionUpdatedIntegrationEvent`) to track session lifecycle transitions (`Cleared`, `Deleted`, `Archived`).

### 4.6 YggdrasilService (Aspire Orchestrator)
- **Role:** Distributed application host (`YggdrasilService.AppHost`) managing startup dependencies, container networking for PostgreSQL and RabbitMQ, and centralized service defaults (`YggdrasilService.ServiceDefaults`) for telemetry, logging, and health endpoints.

### 4.7 Shared Libraries (`src/Bifrost/Backend/Shared`)
- **`Shared.Domain`:** DDD base abstractions (`Entity<TId>`, `AggregateRoot<TId>`, `IAggregateRoot`, `IDomainEvent`, `DomainEvent`).
- **`Shared.Application`:** Messaging abstractions (`IMessageBus`, `MessageEnvelope<T>`, `MessageRoutes`), Outbox interfaces (`OutgoingMessage`), and Unit of Work abstractions (`IUnitOfWork`, `IUnitOfWork<TContext>`, `UnitOfWorkConcurrencyException`).
- **`Shared.Infrastructure`:**
  - `RabbitMqMessageBus`: RabbitMQ client wrapper with OpenTelemetry distributed activity tracing.
  - `OutboxDispatcher<TDbContext>`: Background worker polling pending outbox messages and publishing to RabbitMQ with retry backoff and tracing propagation.
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
| `requests` | `request.created` | `HelpRequestCreatedMessage`<br>*(Message, SessionId, Generation)* | `EirService.HelpRequests` (Outbox) | `MimirService` |
| `requests` | `request.updated` | `HelpRequestUpdatedIntegrationEvent`<br>*(RequestId, SessionId, Generation, Status)* | `EirService.HelpRequests` (Outbox) | `GnaService` |
| `requests` | `request.answered` | `HelpRequestAnsweredMessage`<br>*(RequestId, SessionId, Generation, Message)* | `EirService.HelpRequests` (Outbox) | `MimirService` |
| `requests` | `generalized.created` | `GeneralizedCreatedMessage`<br>*(Message, SessionId, Generation)* | `MimirService` (Outbox) | `EirService.HelpRequests` |
| `notifications` | `gna-notification-requested` | `NotificationCreatedMessage`<br>*(UserId, Messages, SessionId, Generation)* | External / Domain Triggers | `GnaService` |

---

## 6. ReviewBot Review Guidelines & Checklist

When evaluating Pull Requests, the AI Reviewer must verify:
1. **Clean Architecture Adherence:** No leaking of infrastructure concepts (EF Core, SQL, HTTP contexts) into Application or Domain layers.
2. **Modular Monolith Isolation in EirService:** No cross-module database contexts, navigation properties, or cross-project dependencies between module internals.
3. **Transactional Outbox Usage:** All integration events must be enqueued via `UnitOfWork` / domain event notification rather than published synchronously to RabbitMQ.
4. **FastEndpoints Pattern:** HTTP endpoints must follow the REPR pattern and validate inputs using FluentValidation classes rather than inline controller logic.
5. **Concurrency & Generations:** Changes affecting session lifecycle must handle `CurrentGeneration` concurrency tokens correctly.
6. **English in Codebase:** All code, identifiers, comments, and log messages must be in English.
7. **Frontend Architecture & Testing:**
   - Feature isolation: no cross-feature imports of internal code.
   - No legacy Angular patterns (`NgModule`, constructor injection, `*ngIf`, Karma).
   - Unit tests required for all services and business logic; skip tests for pure shell components.
   - Cookie-based auth only — no tokens in `localStorage` or `sessionStorage`.
   - English codebase rule applies to TypeScript as well.

---

## Update 2026-10-14

- The Angular frontend (`glitnir-frontend`) is fully implemented, replacing legacy prototypes and integrating into .NET Aspire orchestration via `.esproj` and `AddJavaScriptApp`.
- The frontend architecture enforces a strict feature-based layout (`core/`, `shared/`, `features/`) where features remain isolated and communicate exclusively through public APIs.
- Role-based client-side routing and access control are enforced using Angular `CanMatchFn` guards (`studentGuard` and `teacherGuard`) that handle redirection and lazy-loaded feature boundaries.
- Frontend state management and backend communication rely on Angular signals, service singletons, and HTTP clients configured with credentials (`withCredentials: true`) to support cookie-based authentication.
- Review guidelines and project context were updated to replace frontend placeholders with explicit architectural and testing rules, including mandatory unit testing for business logic and prohibition of legacy Angular patterns.

---

## Update 2026-10-16

- `MimirService` help request entities and database persistence now explicitly track `SessionId` and `Generation` for proper session lifecycle alignment.
- `MimirService` message handlers enforce idempotency by verifying request existence prior to processing, avoiding redundant LLM calls on message redeliveries.
- `MimirService` incorporates fallback resilience (`LlmUnavailableException`), gracefully forwarding original student queries if all configured LLM models fail.
- Core integration message contracts (`HelpRequestCreatedMessage` and `GeneralizedCreatedMessage`) now strictly mandate `HelpRequestId`, `SessionId`, and `Generation`.
- Session generation query handlers within `EirService` utilize explicit service scope factories to guarantee safe isolation and resource disposal.

---

## Update 2026-10-19

- `GnaService` integrates real-time SignalR hubs (`/api/students/notifications` and `/api/teachers/notifications`) for pushing notifications to connected clients.
- SignalR connection authentication supports both student identity cookies and JWT Bearer tokens passed via query parameters (`access_token`).
- A multi-channel `NotificationDispatcher` in `GnaService` attempts delivery via active SignalR connections before falling back to push notification channels.
- `GnaService` persistence introduces `PushSubscription` entities and extended `Notification` metadata attributes for tracking delivery context.
- Outbox dispatcher polling intervals for `EirService.HelpRequests` and `GnaService` are tuned to 500ms to minimize integration event propagation latency.
- The test frontend includes reactive SignalR client subscriptions for real-time notification handling and state synchronization.
