export type NotificationRole = 'students' | 'teachers';
export type NotificationEventType = 'Unknown' | 'HelpRequestCreated' | 'StudentMessageAdded'
  | 'HelpRequestStarted' | 'HelpRequestAnswered' | 'FaqCreated' | 'FaqAnswered';

export interface Notification {
  notificationId: string;
  userId: string;
  message: string;
  createdAt: string;
  sessionId?: string | null;
  helpRequestId?: string | null;
  generation?: number | null;
  change?: string | null;
  status?: string | null;
  eventType?: NotificationEventType;
  // Set by the connection that received the notification.
  recipientRole?: NotificationRole;
}

const titles: Record<NotificationEventType, string> = {
  Unknown: 'Ny notifikation',
  HelpRequestCreated: 'Nyt spørgsmål i køen',
  StudentMessageAdded: 'Opfølgende spørgsmål i køen',
  HelpRequestStarted: 'Det er din tur til hjælp',
  HelpRequestAnswered: 'Underviseren har svaret',
  FaqCreated: 'Nyt fælles FAQ-spørgsmål',
  FaqAnswered: 'Svar på dit fælles FAQ-spørgsmål'
};

export function notificationTitle(notification: Notification | null): string {
  return titles[notification?.eventType ?? 'Unknown'] ?? titles.Unknown;
}

export function helpRequestId(value: string | null | undefined): string | null {
  return value && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value)
    && value !== '00000000-0000-0000-0000-000000000000' ? value : null;
}

export function notificationTarget(notification: Notification | null): string | null {
  if (!notification?.recipientRole || !helpRequestId(notification.sessionId)) return null;
  const target = `/${notification.recipientRole}/sessions/${notification.sessionId}`;
  return helpRequestId(notification.helpRequestId) ? target + '?requestId=' + notification.helpRequestId : target;
}

export function notificationAffectsSession(notification: Notification, role: NotificationRole,
  sessionId: string, generation?: number): boolean {
  if (notification.recipientRole && notification.recipientRole !== role) return false;
  if (!sessionId || !notification.sessionId) return true;
  return notification.sessionId.toLowerCase() === sessionId.toLowerCase()
    && (generation == null || notification.generation == null || notification.generation === generation);
}
