export type HelpType = 'physical' | 'digital';

export type HelpRequestStatus =
  | 'pending'
  | 'active'
  | 'awaitingConfirmation'
  | 'awaitingFaq'
  | 'completedByFaq'
  | 'completedByDigital'
  | 'completedByPhysical'
  | 'cancelled';

export interface CreateHelpRequestInput {
  message: string;
  helpType: HelpType;
}

export interface HelpRequest {
  id: string;
  displayNumber: string;
  status: HelpRequestStatus;
  message: string;
  helpType: HelpType;
  requestNumber: number;
}

export interface SessionQueue {
  sessionId: string;
  generation: number;
  waitingCount: number;
  unreservedWaitingCount: number;
  ownRequestId: string | null;
  position: number | null;
  estimatedWaitMinutes: number | null;
  requestNumber: number | null;
  displayNumber: string | null;
}

export interface TeacherHelpRequest {
  id: string;
}

export interface TeacherQueue {
  waitingCount: number;
  unreservedWaitingCount: number;
  generation: number;
  frontQueuedAt: string | null;
  myReservedRequestId: string | null;
}
