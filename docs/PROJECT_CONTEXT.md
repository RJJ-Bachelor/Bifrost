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
- **API Framework:** FastEndpoints 8.3.0 (REPR pattern: Request-Endpoint-Response) for API endpoints, internal in-process commands, and domain event notifications.
- **Validation:** FluentValidation 12.1.1 integrated into FastEndpoints via pipeline command middleware (`CommandValidationMiddleware`).
- **Gateway & Reverse Proxy:** YARP (Yet Another Reverse Proxy) in `HeimdallGateway` managing edge routing, token extraction, and student cookie issuance.
- **Identity & Authentication:**
  - **Teachers:** Duende IdentityServer (in `VarService`) with ASP.NET Core Identity for OAuth 2.0 / OpenID Connect authorization code flow with PKCE and JWT Bearer tokens.
  - **Students:** ASP.NET Core Data Protection encrypted cookie authentication (`bifrost-student`) issuing anonymous student identifiers.
- **Databases & Persistence:**
  - PostgreSQL 16+ via Entity Framework Core 10 (`10.0.12`) and Npgsql.
  - Isolated database per service (`eir`, `gna`, `mimir`, `var`).
  - Isolated database schema per module in `EirService` (`sessions`, `participants`, `helprequests`).
- **Message Broker & Event-Driven Architecture:**
  - RabbitMQ 4+ (`RabbitMQ.Client 7.1.2`).
  - Custom abstraction `IMessageBus` (`RabbitMqMessageBus`) with W3C distributed trace context propagation via OpenTelemetry `ActivitySource`.
  - Transactional Outbox Pattern (`OutboxDispatcher`, `OutboxMessage`, `OutgoingMessage`) for reliable at-least-once message dispatching.
  - Unit of Work pattern (`IUnitOfWork`, `IUnitOfWork<TContext>`) guaranteeing atomic persistence of aggregate changes and outbox messages.
- **Frontend (Status: Pending Rewrite):**
  - Current frontend projects (`GlitnirFrontend`, `TestFrontend`) are legacy/temporary prototypes and are currently excluded from architectural requirements while being redesigned. Frontend architecture will be defined in a future milestone.
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
     - **Aggregate:** `HelpRequest` (Id, `UserId`, `Message`, `SessionId`, `Generation`).
     - **Endpoints:**
       - Student: `POST /api/students/helprequests` (Submit help request; stages `HelpRequestCreatedMessage` into outbox), `GET /api/students/helprequests` (List help requests).
     - **Messaging Consumers:**
       - `GeneralizedMessageSubscriber`: Consumes `requests / generalized.created` from `MimirService`.
       - `SessionUpdatedMessageSubscriber`: Consumes `sessions / session.updated` to invalidate or clean up requests on session deletion.
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
7. **Frontend Exemption:** Ignore legacy frontend projects until the new frontend architecture is implemented.
