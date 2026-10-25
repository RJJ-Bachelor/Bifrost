export type HelpType = 'physical' | 'digital';

export type HelpRequestStatus =
  | 'pending'
  | 'active'
  | 'awaitingConfirmation'
  | 'completed'
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
  ownRequestId: string | null;
}

export interface TeacherHelpRequest {
  id: string;
}

export interface TeacherQueueEntry {
  id: string;
  assignedTeacherId: string | null;
}

export interface TeacherQueue {
  waitingCount: number;
  availableCount: number;
  generation: number;
  frontQueuedAt: string | null;
  pendingRequests: TeacherQueueEntry[];
  activeRequests: TeacherQueueEntry[];
}
