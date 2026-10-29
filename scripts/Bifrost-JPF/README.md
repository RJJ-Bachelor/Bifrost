# Bifrost in Bruno

Open `scripts/Bifrost-JPF` as a collection in Bruno, and select **Local** and **Developer Mode**.
The collection's shared response script automatically saves values after successful requests:

| Request | Variables for subsequent requests |
|---|---|
| CreateSession / JoinSession / GetSession | `sessionId`, `sessionGeneration`, and any session codes included in the response |
| Student CreateHelpRequest / GetHelpRequestById / message and status | `studentHelpRequestId` and its alias, `helpRequestId` |
| Teacher TakeNextHelpRequest / GetHelpRequest / answer and status | `teacherHelpRequestId`; a FAQ also saves `generalizationId` |
| ArchiveSession | `archiveId` and the new `sessionGeneration`; old request and demo IDs are cleared |
| ClearSession | The new `sessionGeneration`; old request and generalization IDs are cleared |
| DeleteSession | The selected session and its request IDs are cleared |

Values are set both as runtime variables and in the selected environment, so they can be used
immediately and inspected under Local. Student requests use `studentHelpRequestId`,
and teacher requests use `teacherHelpRequestId`. Reserving a FAQ therefore does not overwrite
the student's ID for AcceptFaq/RejectFaq. CreateHelpRequest also selects the first request
for teacher inspection; TakeNextHelpRequest then selects the actual reservation.
List requests do not automatically select an arbitrary item.

## Development

**Development is optional.** The ordinary session, archive, queue, and conversation requests are
under **EirService** and can be used without seeding or the Development folder.
Test helpers are grouped by purpose:

| Folder | Purpose |
|---|---|
| Development → FAQ demo | Create test data and try a shared FAQ with two different students |
| Development → Workflows | Create a new session and a digital HelpRequest through the normal endpoints |
| MimirService | Send a test generalization through Mimir and RabbitMQ, using seeded data or your own data |

Only **Seed FAQ demo** calls Eir's dedicated Development endpoint. The other demo requests
use the normal endpoints with validated demo IDs and the correct student cookies.
The Workflows request automates ordinary login, session creation, joining, and request creation.
Mimir's two test endpoints and Eir's seed are available only in the `Development` service environment and require teacher login.

For existing data, use **MimirService → Generalize own help requests**.
Create at least two HelpRequests in the same session and generation, and insert their IDs
into the request's JSON body. Seeding is optional, and `sessionId` and `sessionGeneration` use the ordinary session variables.
See [the workflow with your own data](Development/README.md#generalizing-your-own-helprequests)
or [the complete FAQ demo](Development/README.md#getting-started).

## Folders

EirService follows the API layer's role and module folders. Student read requests for
HelpRequests are grouped in one folder:

```text
EirService/
├── Monitoring/
│   └── Alive/
├── Student/
│   ├── Participants/
│   │   ├── GetStudentIdentity/
│   │   ├── JoinSession/
│   │   └── GetStudentSessions/
│   ├── Sessions/
│   │   ├── GetStudentSession/
│   │   ├── GetSessionArchives/
│   │   └── GetSessionArchive/
│   ├── HelpRequests/
│   │   ├── CreateHelpRequest/
│   │   ├── GetHelpRequests/
│   │   │   ├── GetHelpRequests.yml
│   │   │   ├── GetHelpRequestsForSession.yml
│   │   │   └── GetHelpRequestById.yml
│   │   ├── GetStudentQueue/
│   │   ├── GetStudentQueueStatus/
│   │   ├── AddStudentMessage/
│   │   ├── CancelHelpRequest/
│   │   └── RespondToFaq/
│   │       ├── AcceptFaq.yml
│   │       └── RejectFaq.yml
│   └── KnowledgeBase/
│       └── GetStudentFaqs/
└── Teacher/
    ├── Participants/
    │   ├── GetTeacherIdentity/
    │   └── JoinSession/
    ├── Sessions/
    │   ├── CreateSession/
    │   ├── GetSessions/
    │   ├── GetSession/
    │   ├── ArchiveSession/
    │   ├── GetSessionArchives/
    │   ├── GetSessionArchive/
    │   ├── ClearSession/
    │   └── DeleteSession/
    ├── HelpRequests/
    │   ├── GetQueue/
    │   ├── GetTeacherQueueStatus/
    │   ├── GetHelpRequest/
    │   ├── TakeNextHelpRequest/
    │   ├── AnswerHelpRequest/
    │   └── ChangeHelpRequestStatus/
    ├── KnowledgeBase/
    │   ├── GetTeacherFaqs/
    │   └── CreateFaq/
    └── Insights/
        └── GetSessionStatistics/
```

The `GetHelpRequests` folder groups the list, the list filtered by session, and a single conversation
by ID. List requests show your requests in each session's current generation.
`GetHelpRequestById` retrieves the status and all conversation messages and can also be used
for a request from before ClearSession.
Teacher sessions return the creator's ID as `createdByTeacherId`.
Other teachers can use `Teacher/Participants/JoinSession` with `teacherSessionCode`.
`sessionCode` is only for students; teachers must join using the separate `teacherCode`.
`GetSessions` then shows both created and joined sessions. Associated
teachers share the queue, while each reservation belongs to the teacher who took it.
Each session in `GetSessions` has `waitingCount` for its current generation, including when the count is zero.
It matches `GetQueue`: all `Pending` requests, including reserved requests and FAQ questions.
Requests with `Active`, `AwaitingFaq`, or `AwaitingConfirmation` are not counted.

## Login

- **Student:** The first student request through Heimdall automatically creates the student cookie.
  `Login/Student/Login` can still be used to inspect the identity; Bruno saves and reuses the cookie.
- **Teacher:** Select **Developer Mode** using the shield icon, and run `Login/Teacher/Login`.
  The existing login helper saves the teacher cookie in Bruno.

`teacherUsername` and `teacherPassword` follow the existing setup.
A populated password in Local takes precedence; an empty password uses the collection's value.
Ordinary Eir requests go through Heimdall at `{{gatewayUrl}}`, normally `http://localhost:6004`.

## Session variables in Local

| Variable | Value |
|---|---|
| `sessionId` | Saved automatically from CreateSession, JoinSession, and GetSession |
| `sessionCode` | Saved from teacher CreateSession/GetSession and student JoinSession |
| `teacherSessionCode` | Saved from teacher CreateSession/GetSession and teacher JoinSession |
| `sessionGeneration` | The current generation; updated after ArchiveSession and ClearSession |
| `archiveId` | Saved from ArchiveSession and archive details; select an older archive using its ID in the detail request's URL |
| `studentHelpRequestId` / `helpRequestId` | The student's request; saved from student create, read, and mutation requests |
| `teacherHelpRequestId` | Saved from teacher read, reservation, and mutation requests |
| `generalizationId` | The shared FAQ question's ID; saved from seeding, both Mimir test requests, and FAQ answers |
| `firstHelpRequestId` / `secondHelpRequestId` | The two original student requests from seeding |

Variables are populated automatically in runtime and the selected environment after successful requests.
For an existing frontend session, call GetSession once with its actual ID in the URL;
the response selects the session and generation for subsequent requests. A successful
**Generalize own help requests** also automatically selects the session from the submitted payload.

## Teacher

| Request | Method and route |
|---|---|
| GetTeacherIdentity | `GET /api/teachers/me` |
| JoinTeacherSession | `POST /api/teachers/sessions/join` with teacher code |
| CreateSession | `POST /api/teachers/sessions` with title |
| GetSessions | `GET /api/teachers/sessions` |
| GetSession | `GET /api/teachers/sessions/{{sessionId}}` |
| ArchiveSession | `POST /api/teachers/sessions/{{sessionId}}/archive` with expected generation |
| GetSessionArchives | `GET /api/teachers/sessions/{{sessionId}}/archives` |
| GetSessionArchive | `GET /api/teachers/sessions/{{sessionId}}/archives/{{archiveId}}` |
| ClearSession | `POST /api/teachers/sessions/{{sessionId}}/clear` with expected generation |
| DeleteSession | `DELETE /api/teachers/sessions/{{sessionId}}` |
| GetQueue | `GET /api/teachers/sessions/{{sessionId}}/queue` |
| GetTeacherQueueStatus | `GET /api/teachers/sessions/{{sessionId}}/queue/status` |
| GetSessionStatistics | `GET /api/teachers/sessions/{{sessionId}}/statistics` |
| TakeNextHelpRequest | `POST /api/teachers/sessions/{{sessionId}}/queue/take-next` |
| GetHelpRequest | `GET /api/teachers/requests/{{teacherHelpRequestId}}` |
| AnswerHelpRequest / AnswerFaq | `POST /api/teachers/requests/{{teacherHelpRequestId}}/messages` with `text` and optional `question` for a FAQ |
| Start / Complete / Cancel | `PATCH /api/teachers/requests/{{teacherHelpRequestId}}/status` with `action` |
| GetTeacherFaqs | `GET /api/teachers/sessions/{{sessionId}}/faqs` |
| CreateFaq | `POST /api/teachers/sessions/{{sessionId}}/faqs` with `question` and `answer` |

## Student

| Request | Method and route |
|---|---|
| GetStudentIdentity | `GET /api/students/me` |
| JoinSession | `POST /api/students/sessions/join` with session code |
| GetStudentSessions | `GET /api/students/sessions` |
| GetStudentSession | `GET /api/students/sessions/{{sessionId}}` |
| GetSessionArchives | `GET /api/students/sessions/{{sessionId}}/archives` |
| GetSessionArchive | `GET /api/students/sessions/{{sessionId}}/archives/{{archiveId}}` |
| CreateHelpRequest | `POST /api/students/sessions/{{sessionId}}/requests` with `message` and `helpType` |
| GetHelpRequests | `GET /api/students/requests` |
| GetHelpRequestsForSession | `GET /api/students/requests?sessionId={{sessionId}}` |
| GetHelpRequestById | `GET /api/students/requests/{{studentHelpRequestId}}` |
| GetStudentQueue | `GET /api/students/sessions/{{sessionId}}/queue` |
| GetStudentQueueStatus | `GET /api/students/sessions/{{sessionId}}/queue/status` |
| AddStudentMessage | `POST /api/students/requests/{{studentHelpRequestId}}/messages` with `text` |
| CancelHelpRequest | `PATCH /api/students/requests/{{studentHelpRequestId}}/status` with `action: "Cancel"` |
| GetStudentFaqs | `GET /api/students/sessions/{{sessionId}}/faqs` |
| AcceptFaq / RejectFaq | `PATCH /api/students/requests/{{studentHelpRequestId}}/faq` with `accept: true` / `false` |

## Archiving

1. Log in as a teacher, and select the session with **Teacher → Sessions → GetSession**.
2. Run **ArchiveSession**. The body's `expectedGeneration` comes from `sessionGeneration`.
   The response saves `archiveId`, updates the current generation, and clears old request IDs.
3. Use **GetSessionArchives** and **GetSessionArchive** under Teacher or Student → Sessions.
   For an older archive, replace `{{archiveId}}` once in the detail request's URL with the list's `id`;
   the response saves the selected `archiveId`. List requests never automatically select an archive.
4. Check the new generation with **GetSession** and the empty queue with **GetQueue**.
   The same session and existing session codes remain in use.

An archive contains the generation's FAQs and conversations. The teacher can retrieve all
conversations from that generation; students can retrieve their own conversations and answered FAQs.
The archive list includes `privateConversationCount` and `generalizedQuestionCount` for each generation.
The first counts private conversations (HelpRequests), so each conversation is counted once regardless of its message count.
The second counts the generation's generalized FAQ questions. The teacher sees all questions,
while the student's counts include only their own conversations and answered FAQ questions.
Archive details never change `sessionGeneration` to the old generation.
Archiving also sends a message to Mimir through the normal outbox and RabbitMQ.
If another teacher has already changed the generation, ArchiveSession returns `409`;
retrieve GetSession again before retrying.

**ClearSession** remains available as the separate clear action that does not create an archive.
Use **ArchiveSession** to save the generation and make it available in the archive list.

## FAQ and generalization

**Teacher → KnowledgeBase → GetTeacherFaqs** lists pending and answered shared questions as well as teachers' own FAQs.
**Student → KnowledgeBase → GetStudentFaqs** lists only answered questions in the session's current generation.
Both use `sessionId` from Local and the relevant role cookie.

**Teacher → KnowledgeBase → CreateFaq** creates a teacher's own question and answer in one call.
Select the session with **GetSession**, then edit `question` and `answer` directly in the JSON body.
Both fields are required. The server selects the session's current generation; do not send a
generation or student request IDs. `201 Created` returns the answered FAQ with `sourceCount: 0`.
It appears immediately in both FAQ lists and requires neither a queue reservation nor student acceptance.
Archiving includes it in that generation's archive; after ArchiveSession or ClearSession, the
FAQ lists show only questions from the new generation.

The teacher reserves a FAQ through **Teacher → HelpRequests → TakeNextHelpRequest**
and answers it through **AnswerHelpRequest**. **AnswerFaq (edit question)** sends
`text` and `question` together, allowing the question to be revised when answering. Omit `question`
to preserve the existing wording. This field is only accepted for a FAQ; ordinary digital
help still uses only `text`. `teacherHelpRequestId` is automatically set to the returned
FAQ's ID, which is also its `generalizationId`. The reservation must belong to the current
teacher and the session's current generation.

The student responds through **Student → HelpRequests → RespondToFaq → AcceptFaq / RejectFaq**.
Here, `studentHelpRequestId` is the student's **original request**, and the student cookie must belong to that request.
Physical and digital requests can both receive the FAQ answer in their conversation.
Accepting completes the request; rejecting returns it to the queue with the same queue time and help type,
so a physical request continues to wait for physical help. Responding requires status
`AwaitingConfirmation`, and each student chooses one of the options.

Use **Development → FAQ demo** for the complete seeded workflow: its requests select IDs and student cookies automatically.
FAQ lists are under the ordinary EirService folders. The reusable question and answer
from the queue reach KnowledgeBase asynchronously, so the student's list may be empty immediately after answering.

## Queue and conversations

CreateHelpRequest receives its ID, generation, `requestNumber`, and `displayNumber` from the server.
The display number, such as `BF-001`, stays with the request when it is returned or reopened.
Numbers restart in each session and after ArchiveSession or ClearSession. Continue using the GUID
as `helpRequestId`; the queue position is a separate number.
Queue and conversation requests are
under `EirService/Student/HelpRequests` and `EirService/Teacher/HelpRequests`.
`GetStudentQueueStatus` shows `waitingCount` and `estimatedWaitMinutes` for a new request,
even before the student is in the queue; the student must first join the session.
The estimate is `null` until the current generation has a positive handling-time measurement from a
completed request, even if the queue is empty. Digital handling time is measured from Take next
(reservation) to answer; physical handling time is measured from Start to Complete. With usable
measurements and no waiting or active requests, the estimate is `0` minutes.
`GetTeacherQueueStatus` shows `waitingCount`, `unreservedWaitingCount`, `frontWaitMinutes`,
`generation`, and `myReservedRequestId` without conversations or messages. `waitingCount` counts all
`Pending` requests, while `unreservedWaitingCount` counts only those without an assigned teacher.
`myReservedRequestId` is the current teacher's reserved or active request in the session's
current generation, or `null`. The full `GetQueue` also includes the two counts and this ID,
along with `requests` and `activeRequests` when request lists are needed.
The front request's waiting time is `null` when no one is waiting. Both status requests apply to the current generation.
`TakeNextHelpRequest` reserves a new request only if the teacher does not already have a
reserved (`Pending`) or active (`Active`) request in the same session and generation. Repeated
requests return the existing request, including during physical help. If the teacher neither
has a request in progress nor can take an available one, the endpoint returns `204`.
**Teacher → Insights → GetSessionStatistics** shows today's statistics for the same generation:
completed physical/digital requests, unique students helped, and the average of
`ClosedAt - QueuedAt` in minutes. The average is `null` when no requests have been completed.
Completion statuses are `CompletedByFaq` (student FAQ acceptance), `CompletedByDigital`
(a digital answer, including to a FAQ task), and `CompletedByPhysical` (completed physical help).
All three count as completed requests in the statistics and allow a new student follow-up.
Student requests use the student cookie; teacher requests use teacher login.
They can use the same cookie jar without logging the teacher out.
Start, Complete, and Cancel are in the teacher's `ChangeHelpRequestStatus` folder
and use the same endpoint. Teacher Cancel releases the reservation and returns
the request to the front of the queue with `Pending`. Student Cancel closes the request
with `Cancelled` and removes it from the queue.
See the [Development guide](Development/README.md) for the FAQ test workflow.

A typical workflow is: teacher login → CreateSession → student login → JoinSession → CreateHelpRequest → GetHelpRequestsForSession.
The session ID, session code, and request ID are carried over automatically. ClearSession also updates the generation automatically.

All conversation messages include `messageType` as an enum string:

| `messageType` | Meaning |
|---|---|
| `StudentQuestion` | The student's original question |
| `FollowUpQuestion` | A subsequent question from the student |
| `FaqQuestion` | The generalized shared question from Mimir |
| `FaqAnswer` | The teacher's shared FAQ answer, including when copied to the student's conversation |
| `TeacherAnswer` | The teacher's direct answer to the student's request |

`authorRole` still identifies the sender's role. The server determines the message type;
the request body still uses `message` or `text`. Existing messages receive types
through a database migration the next time EirService starts.

Monitoring, Heimdall, VarService, and Logout remain in their existing folders.

## Start services

Start Docker Desktop, and run from the repository root:

```powershell
aspire.cmd start --apphost src/Bifrost/Orchestration/YggdrasilService/YggdrasilService.AppHost/YggdrasilService.AppHost.csproj --launch-profile http --non-interactive
aspire.cmd wait varservice --timeout 60 --non-interactive
aspire.cmd wait heimdallgateway --timeout 60 --non-interactive
```

## Student authentication

Heimdall issues `bifrost-anonymous-user` and forwards it to the service with the very first request.
Eir, Mimir, and Gna validate the cookie; direct student requests without a valid cookie receive 401.
Student login through VarService is not required.

Old Eir cookies are replaced on the first request after this change. This creates a new student ID,
so existing session memberships must be recreated with JoinSession.
