import { httpResource, HttpResourceRef } from '@angular/common/http';
import { Service } from '@angular/core';
import {
  ArchivedConversation,
  ArchivedConversationMessage,
  ArchivedFaq,
  SessionArchiveDetail,
} from '../archive.model';
import {
  normalizeAuthorRole,
  normalizeHelpType,
  normalizeMessageType,
  normalizeStatus,
} from '../../help-request';

export type ArchiveViewerRole = 'teacher' | 'student';

interface ArchivedFaqDto {
  id: string;
  sessionId: string;
  generation: number;
  question: string;
  answer: string | null;
  sourceCount: number;
  createdAt: string;
  answeredAt: string | null;
}

interface ArchivedConversationMessageDto {
  id: string;
  helpRequestId: string;
  authorId: string;
  authorRole: string;
  messageType: string;
  text: string;
  createdAt: string;
  sequenceNumber: number;
}

interface ArchivedConversationDto {
  id: string;
  sessionId: string;
  generation: number;
  requestNumber: number;
  displayNumber: string;
  studentId: string;
  helpType: string;
  status: string;
  assignedTeacherId: string | null;
  createdAt: string;
  queuedAt: string;
  returnedToQueueAt: string | null;
  startedAt: string | null;
  closedAt: string | null;
  faqAnswerId: string | null;
  messages: ArchivedConversationMessageDto[];
}

interface SessionArchiveDetailDto {
  id: string;
  sessionId: string;
  title: string;
  generation: number;
  startedAtUtc: string;
  archivedAtUtc: string;
  faqs: ArchivedFaqDto[];
  conversations: ArchivedConversationDto[];
}


function mapFaq(dto: ArchivedFaqDto): ArchivedFaq {
  return {
    id: dto.id,
    sessionId: dto.sessionId,
    generation: dto.generation,
    question: dto.question,
    answer: dto.answer,
    sourceCount: dto.sourceCount,
    createdAt: dto.createdAt,
    answeredAt: dto.answeredAt,
  };
}

function mapMessage(dto: ArchivedConversationMessageDto): ArchivedConversationMessage {
  return {
    id: dto.id,
    helpRequestId: dto.helpRequestId,
    authorId: dto.authorId,
    authorRole: normalizeAuthorRole(dto.authorRole),
    messageType: normalizeMessageType(dto.messageType),
    text: dto.text,
    createdAt: dto.createdAt,
    sequenceNumber: dto.sequenceNumber,
  };
}

function mapConversation(dto: ArchivedConversationDto): ArchivedConversation {
  return {
    id: dto.id,
    sessionId: dto.sessionId,
    generation: dto.generation,
    requestNumber: dto.requestNumber,
    displayNumber: dto.displayNumber,
    studentId: dto.studentId,
    helpType: normalizeHelpType(dto.helpType),
    status: normalizeStatus(dto.status),
    assignedTeacherId: dto.assignedTeacherId,
    createdAt: dto.createdAt,
    queuedAt: dto.queuedAt,
    returnedToQueueAt: dto.returnedToQueueAt,
    startedAt: dto.startedAt,
    closedAt: dto.closedAt,
    faqAnswerId: dto.faqAnswerId,
    messages: dto.messages.map(mapMessage),
  };
}

function mapDetail(dto: SessionArchiveDetailDto): SessionArchiveDetail {
  return {
    id: dto.id,
    sessionId: dto.sessionId,
    title: dto.title,
    generation: dto.generation,
    startedAtUtc: dto.startedAtUtc,
    archivedAtUtc: dto.archivedAtUtc,
    faqs: dto.faqs.map(mapFaq),
    conversations: dto.conversations.map(mapConversation),
  };
}

@Service()
export class ArchiveApi {
  getArchive(
    viewerRole: () => ArchiveViewerRole,
    sessionId: () => string,
    archiveId: () => string,
  ): HttpResourceRef<SessionArchiveDetail | undefined> {
    return httpResource<SessionArchiveDetail>(
      () => {
        const audience = viewerRole() === 'teacher' ? 'teachers' : 'students';
        return {
          url: `/api/${audience}/sessions/${sessionId()}/archives/${archiveId()}`,
          method: 'GET' as const,
          withCredentials: true,
        };
      },
      {
        parse: (raw) => mapDetail(raw as SessionArchiveDetailDto),
      },
    );
  }
}
