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
  Completed: 'completed',
  Cancelled: 'cancelled',
};

const HELP_TYPE_MAP: Record<string, HelpType> = {
  Digital: 'digital',
  Physical: 'physical',
};

interface TeacherQueueDto {
  generation: number;
  waitingCount: number;
  requests: Array<{ id: string; queuedAt: string }>;
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
    return httpResource<SessionQueue>(() => ({
      url: `/api/students/sessions/${sessionId()}/queue`,
      method: 'GET',
      withCredentials: true,
    }), { defaultValue: { ownRequestId: null } });
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
          generation: 0,
          frontQueuedAt: null,
        },
        parse: (raw) => {
          const dto = raw as TeacherQueueDto;
          return {
            waitingCount: dto.waitingCount,
            generation: dto.generation,
            frontQueuedAt: dto.requests[0]?.queuedAt ?? null,
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
