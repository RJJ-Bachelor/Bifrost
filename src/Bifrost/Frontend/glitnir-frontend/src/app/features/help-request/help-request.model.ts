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
