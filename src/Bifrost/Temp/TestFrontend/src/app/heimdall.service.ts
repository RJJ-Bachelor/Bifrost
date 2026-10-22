import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';

export interface Identity {
  userId: string;
  roles: string[];
  name?: string | null;
}

export interface SessionSummary {
  id: string;
  title: string;
  createdAtUtc: string;
  currentGeneration: number;
  lastClearedAtUtc: string | null;
}

export interface TeacherSession extends SessionSummary {
  code: string;
  teacherCode: string;
  createdByTeacherId: string;
  createdByDisplayName: string | null;
}

export interface ClearSessionResult {
  sessionId: string;
  currentGeneration: number;
  lastClearedAtUtc: string;
}

export type HelpType = 'Physical' | 'Digital';
export type HelpRequestStatus = 'Pending' | 'Active' | 'AwaitingConfirmation' | 'Completed' | 'Cancelled' | 'AwaitingFaq';
export type HelpRequestAction = 'Start' | 'Complete' | 'Cancel';

export interface HelpRequestSummary {
  id: string;
  requestNumber: number;
  displayNumber: string;
  studentId: string;
  message: string;
  sessionId: string;
  generation: number;
  status: HelpRequestStatus;
  helpType: HelpType;
}

export type HelpRequestMessageType = 'StudentQuestion' | 'FollowUpQuestion' | 'FaqQuestion' | 'FaqAnswer' | 'TeacherAnswer';

export interface HelpRequestMessage {
  id: string;
  helpRequestId: string;
  authorId: string;
  authorRole: 'Student' | 'Teacher' | 'System';
  messageType: HelpRequestMessageType;
  text: string;
  createdAt: string;
  sequenceNumber: number;
}

export interface HelpRequest {
  id: string;
  requestNumber: number;
  displayNumber: string;
  sessionId: string;
  generation: number;
  studentId: string | null;
  isFaq: boolean;
  helpType: HelpType;
  status: HelpRequestStatus;
  assignedTeacherId: string | null;
  createdAt: string;
  queuedAt: string;
  returnedToQueueAt: string | null;
  startedAt: string | null;
  closedAt: string | null;
  faqAnswerId: string | null;
  messages: HelpRequestMessage[];
}

export interface StudentQueue {
  sessionId: string;
  generation: number;
  waitingCount: number;
  ownRequestId: string | null;
  requestNumber: number | null;
  displayNumber: string | null;
  position: number | null;
  estimatedWaitMinutes: number | null;
}

export interface TeacherQueue {
  sessionId: string;
  generation: number;
  waitingCount: number;
  requests: HelpRequest[];
  activeRequests: HelpRequest[];
}

export interface Faq {
  id: string;
  sessionId: string;
  generation: number;
  question: string;
  answer: string | null;
  sourceCount: number;
  createdAt: string;
  answeredAt: string | null;
}

@Injectable({ providedIn: 'root' })
export class HeimdallService {
  private readonly http = inject(HttpClient);

  get<T>(path: string) { return this.http.get<T>('/api/' + path, { withCredentials: true }); }
  post<T>(path: string, body: unknown) { return this.http.post<T>('/api/' + path, body, { withCredentials: true }); }
  patch<T>(path: string, body: unknown) { return this.http.patch<T>('/api/' + path, body, { withCredentials: true }); }

  login(returnPath = '/teachers') {
    window.location.assign('http://localhost:6004/account/login?returnUrl=' + encodeURIComponent(window.location.origin + returnPath));
  }
  logout() {
    window.location.assign('http://localhost:6004/account/logout?returnUrl=' + encodeURIComponent(window.location.origin + '/teachers'));
  }
}
