export type HelpType = 'physical' | 'digital';

export type TeacherStatusAction = 'start' | 'complete' | 'cancel';

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
  queuedAt: string;
  assignedTeacherName: string | null;
}

export type AuthorRole = 'student' | 'teacher' | 'system';

export type HelpRequestMessageType =
  | 'studentQuestion'
  | 'followUpQuestion'
  | 'faqQuestion'
  | 'faqAnswer'
  | 'teacherAnswer';

export interface HelpRequestMessage {
  id: string;
  authorRole: AuthorRole;
  messageType: HelpRequestMessageType;
  text: string;
  createdAt: string;
  sequenceNumber: number;
}

export interface HelpRequestDetail extends HelpRequest {
  messages: HelpRequestMessage[];
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
