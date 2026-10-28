import { HttpClient, httpResource, HttpResourceRef } from '@angular/common/http';
import { Service, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  CreateHelpRequestInput,
  HelpRequest,
  HelpRequestStatus,
  HelpType,
  SessionQueue,
  TeacherHelpRequest,
  TeacherQueue,
  TeacherQueueEntry,
} from '../help-request.model';

interface HelpRequestDto {
  id: string;
  displayNumber: string;
  status: string;
  message: string;
  helpType: string;
  requestNumber: number;
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

interface TeacherQueueEntryDto {
  id: string;
  queuedAt: string;
  assignedTeacherId: string | null;
}

interface TeacherQueueDto {
  generation: number;
  waitingCount: number;
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
        parse: (raw) =>
          (raw as HelpRequestDto[]).map((item) => ({
            id: item.id,
            displayNumber: item.displayNumber,
            status: normalizeStatus(item.status),
            message: item.message,
            helpType: normalizeHelpType(item.helpType),
            requestNumber: item.requestNumber,
          })),
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
          availableCount: 0,
          generation: 0,
          frontQueuedAt: null,
          pendingRequests: [],
          activeRequests: [],
        },
        parse: (raw) => {
          const dto = raw as TeacherQueueDto;
          const requests = dto.requests ?? [];
          const activeRequests = dto.activeRequests ?? [];
          // TODO(BF-46): read availableCount and myReservedRequestId from backend.
          return {
            waitingCount: dto.waitingCount,
            availableCount: requests.filter((item) => item.assignedTeacherId === null).length,
            generation: dto.generation,
            frontQueuedAt: requests[0]?.queuedAt ?? null,
            pendingRequests: requests.map((item) => ({
              id: item.id,
              assignedTeacherId: item.assignedTeacherId,
            })),
            activeRequests: activeRequests.map((item) => ({
              id: item.id,
              assignedTeacherId: item.assignedTeacherId,
            })),
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
}
