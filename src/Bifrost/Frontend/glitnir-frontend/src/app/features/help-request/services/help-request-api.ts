import { HttpClient, httpResource, HttpResourceRef } from '@angular/common/http';
import { Service, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import {
  AuthorRole,
  CreateHelpRequestInput,
  HelpRequest,
  HelpRequestDetail,
  HelpRequestMessage,
  HelpRequestMessageType,
  HelpRequestStatus,
  HelpType,
  SessionQueue,
  TeacherHelpRequest,
  TeacherQueue,
  TeacherStatusAction,
} from '../help-request.model';

interface HelpRequestDto {
  id: string;
  displayNumber: string;
  status: string;
  message: string;
  helpType: string;
  requestNumber: number;
  queuedAt: string;
  assignedTeacherName: string | null;
}

const STATUS_MAP: Record<string, HelpRequestStatus> = {
  Pending: 'pending',
  Active: 'active',
  AwaitingConfirmation: 'awaitingConfirmation',
  AwaitingFaq: 'awaitingFaq',
  CompletedByFaq: 'completedByFaq',
  CompletedByDigital: 'completedByDigital',
  CompletedByPhysical: 'completedByPhysical',
  Cancelled: 'cancelled',
};

const HELP_TYPE_MAP: Record<string, HelpType> = {
  Digital: 'digital',
  Physical: 'physical',
};

const STATUS_ACTION_MAP: Record<TeacherStatusAction, string> = {
  start: 'Start',
  complete: 'Complete',
  cancel: 'Cancel',
};

const AUTHOR_ROLE_MAP: Record<string, AuthorRole> = {
  Student: 'student',
  Teacher: 'teacher',
  System: 'system',
};

const MESSAGE_TYPE_MAP: Record<string, HelpRequestMessageType> = {
  StudentQuestion: 'studentQuestion',
  FollowUpQuestion: 'followUpQuestion',
  FaqQuestion: 'faqQuestion',
  FaqAnswer: 'faqAnswer',
  TeacherAnswer: 'teacherAnswer',
};

interface HelpRequestMessageDto {
  id: string;
  authorRole: string;
  messageType: string;
  text: string;
  createdAt: string;
  sequenceNumber: number;
}

interface HelpRequestDetailDto extends HelpRequestDto {
  messages: HelpRequestMessageDto[];
}

interface TeacherQueueEntryDto {
  id: string;
  queuedAt: string;
  assignedTeacherId: string | null;
}

interface TeacherQueueDto {
  generation: number;
  waitingCount: number;
  unreservedWaitingCount: number;
  myReservedRequestId: string | null;
  requests: TeacherQueueEntryDto[];
  activeRequests: TeacherQueueEntryDto[];
}

function normalizeStatus(raw: string): HelpRequestStatus {
  const mapped = STATUS_MAP[raw];
  if (mapped === undefined) {
    console.warn(`Unknown status "${raw}" from backend, falling back to "pending"`);
    return 'pending';
  }
  return mapped;
}

function normalizeHelpType(raw: string): HelpType {
  const mapped = HELP_TYPE_MAP[raw];
  if (mapped === undefined) {
    console.warn(`Unknown helpType "${raw}" from backend, falling back to "physical"`);
    return 'physical';
  }
  return mapped;
}

function normalizeAuthorRole(raw: string): AuthorRole {
  const mapped = AUTHOR_ROLE_MAP[raw];
  if (mapped === undefined) {
    console.warn(`Unknown authorRole "${raw}" from backend, falling back to "system"`);
    return 'system';
  }
  return mapped;
}

function normalizeMessageType(raw: string): HelpRequestMessageType {
  const mapped = MESSAGE_TYPE_MAP[raw];
  if (mapped === undefined) {
    console.warn(`Unknown messageType "${raw}" from backend, falling back to "teacherAnswer"`);
    return 'teacherAnswer';
  }
  return mapped;
}

function mapMessage(dto: HelpRequestMessageDto): HelpRequestMessage {
  return {
    id: dto.id,
    authorRole: normalizeAuthorRole(dto.authorRole),
    messageType: normalizeMessageType(dto.messageType),
    text: dto.text,
    createdAt: dto.createdAt,
    sequenceNumber: dto.sequenceNumber,
  };
}

function mapHelpRequest(dto: HelpRequestDto): HelpRequest {
  return {
    id: dto.id,
    displayNumber: dto.displayNumber,
    status: normalizeStatus(dto.status),
    message: dto.message,
    helpType: normalizeHelpType(dto.helpType),
    requestNumber: dto.requestNumber,
    queuedAt: dto.queuedAt,
    assignedTeacherName: dto.assignedTeacherName ?? null,
  };
}

function mapDetail(dto: HelpRequestDetailDto): HelpRequestDetail {
  return {
    ...mapHelpRequest(dto),
    messages: dto.messages.map(mapMessage),
  };
}

@Service()
export class HelpRequestApi {
  private readonly http = inject(HttpClient);

  getStudentRequests(sessionId: () => string): HttpResourceRef<HelpRequest[]> {
    return httpResource<HelpRequest[]>(
      () => ({
        url: `/api/students/requests?sessionId=${sessionId()}`,
        method: 'GET',
        withCredentials: true,
      }),
      {
        defaultValue: [],
        parse: (raw) => (raw as HelpRequestDto[]).map(mapHelpRequest),
      },
    );
  }

  getStudentRequest(id: () => string | null): HttpResourceRef<HelpRequestDetail | undefined> {
    return httpResource<HelpRequestDetail>(
      () => {
        const requestId = id();
        if (requestId === null) return undefined;
        return {
          url: `/api/students/requests/${requestId}`,
          method: 'GET',
          withCredentials: true,
        };
      },
      {
        parse: (raw) => mapDetail(raw as HelpRequestDetailDto),
      },
    );
  }

  getStudentQueue(sessionId: () => string): HttpResourceRef<SessionQueue> {
    return httpResource<SessionQueue>(
      () => ({
        url: `/api/students/sessions/${sessionId()}/queue`,
        method: 'GET',
        withCredentials: true,
      }),
      {
        defaultValue: {
          sessionId: '',
          generation: 0,
          waitingCount: 0,
          unreservedWaitingCount: 0,
          ownRequestId: null,
          position: null,
          estimatedWaitMinutes: null,
          requestNumber: null,
          displayNumber: null,
        },
      },
    );
  }

  createHelpRequest(sessionId: string, input: CreateHelpRequestInput): Observable<HelpRequest> {
    return this.http.post<HelpRequest>(
      `/api/students/sessions/${sessionId}/requests`,
      input,
      { withCredentials: true },
    );
  }

  cancelRequest(requestId: string): Observable<HelpRequest> {
    return this.http.patch<HelpRequest>(
      `/api/students/requests/${requestId}/status`,
      { action: 'Cancel' },
      { withCredentials: true },
    );
  }

  respondToFaq(requestId: string, accept: boolean): Observable<HelpRequestDetail> {
    return this.http
      .patch<HelpRequestDetailDto>(
        `/api/students/requests/${requestId}/faq`,
        { accept },
        { withCredentials: true },
      )
      .pipe(map((dto) => mapDetail(dto)));
  }

  addStudentMessage(requestId: string, text: string): Observable<HelpRequestDetail> {
    return this.http
      .post<HelpRequestDetailDto>(
        `/api/students/requests/${requestId}/messages`,
        { text },
        { withCredentials: true },
      )
      .pipe(map((dto) => mapDetail(dto)));
  }

  getTeacherQueue(sessionId: () => string): HttpResourceRef<TeacherQueue> {
    return httpResource<TeacherQueue>(
      () => ({
        url: `/api/teachers/sessions/${sessionId()}/queue`,
        method: 'GET',
        withCredentials: true,
      }),
      {
        defaultValue: {
          waitingCount: 0,
          unreservedWaitingCount: 0,
          generation: 0,
          frontQueuedAt: null,
          myReservedRequestId: null,
        },
        parse: (raw) => {
          const dto = raw as TeacherQueueDto;
          const requests = dto.requests ?? [];
          return {
            waitingCount: dto.waitingCount,
            unreservedWaitingCount: dto.unreservedWaitingCount,
            generation: dto.generation,
            frontQueuedAt: requests[0]?.queuedAt ?? null,
            myReservedRequestId: dto.myReservedRequestId,
          };
        },
      },
    );
  }

  takeNext(sessionId: string): Observable<TeacherHelpRequest | null> {
    return this.http.post<TeacherHelpRequest | null>(
      `/api/teachers/sessions/${sessionId}/queue/take-next`,
      null,
      { withCredentials: true },
    );
  }

  getTeacherRequest(id: () => string | null): HttpResourceRef<HelpRequestDetail | undefined> {
    return httpResource<HelpRequestDetail>(
      () => {
        const requestId = id();
        if (requestId === null) return undefined;
        return {
          url: `/api/teachers/requests/${requestId}`,
          method: 'GET',
          withCredentials: true,
        };
      },
      {
        parse: (raw) => mapDetail(raw as HelpRequestDetailDto),
      },
    );
  }

  answerTeacherRequest(requestId: string, text: string, question?: string): Observable<HelpRequestDetail> {
    return this.http
      .post<HelpRequestDetailDto>(
        `/api/teachers/requests/${requestId}/messages`,
        { text, question },
        { withCredentials: true },
      )
      .pipe(map((dto) => mapDetail(dto)));
  }

  changeTeacherStatus(requestId: string, action: TeacherStatusAction): Observable<HelpRequestDetail> {
    return this.http
      .patch<HelpRequestDetailDto>(
        `/api/teachers/requests/${requestId}/status`,
        { action: STATUS_ACTION_MAP[action] },
        { withCredentials: true },
      )
      .pipe(map((dto) => mapDetail(dto)));
  }
}
