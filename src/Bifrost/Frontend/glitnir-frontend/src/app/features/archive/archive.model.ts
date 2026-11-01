import { AuthorRole, HelpRequestMessageType, HelpRequestStatus, HelpType } from '../help-request';

export interface ArchivedFaq {
  id: string;
  sessionId: string;
  generation: number;
  question: string;
  answer: string | null;
  sourceCount: number;
  createdAt: string;
  answeredAt: string | null;
}

export interface ArchivedConversationMessage {
  id: string;
  helpRequestId: string;
  authorId: string;
  authorRole: AuthorRole;
  messageType: HelpRequestMessageType;
  text: string;
  createdAt: string;
  sequenceNumber: number;
}

export interface ArchivedConversation {
  id: string;
  sessionId: string;
  generation: number;
  requestNumber: number;
  displayNumber: string;
  studentId: string;
  helpType: HelpType;
  status: HelpRequestStatus;
  assignedTeacherId: string | null;
  createdAt: string;
  queuedAt: string;
  returnedToQueueAt: string | null;
  startedAt: string | null;
  closedAt: string | null;
  faqAnswerId: string | null;
  messages: ArchivedConversationMessage[];
}

export interface SessionArchiveDetail {
  id: string;
  sessionId: string;
  title: string;
  generation: number;
  startedAtUtc: string;
  archivedAtUtc: string;
  faqs: ArchivedFaq[];
  conversations: ArchivedConversation[];
}
