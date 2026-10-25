export interface Session {
  id: string;
  title: string;
  currentGeneration: number;
  lastClearedAtUtc: string | null;
  code?: string;
  teacherCode?: string;
}

export interface SessionStatus {
  waitingCount: number;
  estimatedWaitMinutes: number;
}

export interface SessionGeneration {
  id: string;
  sessionId: string;
  title: string;
  generation: number;
  startedAtUtc: string;
  archivedAtUtc: string;
  privateConversationCount: number;
  generalizedQuestionCount: number;
}

export interface TeacherStatistics {
  physicalCount: number;
  digitalCount: number;
  helpedStudentCount: number;
  averageWaitMinutes: number | null;
}

export interface ArchiveResult {
  id: string;
  sessionId: string;
  generation: number;
  currentGeneration: number;
  archivedAtUtc: string;
}
