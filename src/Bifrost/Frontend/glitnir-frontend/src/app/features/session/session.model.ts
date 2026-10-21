export interface Session {
  id: string;
  title: string;
  currentGeneration: number;
  lastClearedAtUtc: string | null;
}
