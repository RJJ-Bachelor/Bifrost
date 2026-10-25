export interface Session {
  id: string;
  title: string;
  currentGeneration: number;
  lastClearedAtUtc: string | null;
}

export interface SessionStatus {
  waitingCount: number;
  estimatedWaitMinutes: number;
}

export interface SessionGeneration {
  generation: number;
  title: string;
}
