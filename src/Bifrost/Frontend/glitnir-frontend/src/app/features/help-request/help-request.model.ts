export type HelpType = 'physical' | 'digital';

export interface CreateHelpRequestInput {
  message: string;
  helpType: HelpType;
}
