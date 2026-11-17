export type UserRole = 'teacher' | 'student';

export interface Identity {
  userId: string;
  name?: string | null;
  roles: string[];
}

export interface IdentityTransferCode {
  code: string;
  expiresAtUtc: string;
}
