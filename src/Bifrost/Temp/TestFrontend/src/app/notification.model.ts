export type NotificationRole = 'students' | 'teachers';
export type NotificationEventType = 'Unknown' | 'HelpRequestCreated' | 'StudentMessageAdded'
  | 'HelpRequestStarted' | 'HelpRequestAnswered' | 'FaqCreated' | 'FaqAnswered' | 'SessionChanged';
export type NotificationDeliveryType = 'RealtimeOrPush' | 'RealtimeOnly';

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
  deliveryType?: NotificationDeliveryType;
  // Set by the connection that received the notification.
  recipientRole?: NotificationRole;
}

const titles: Record<NotificationEventType, string> = {
  Unknown: 'Ny notifikation',
  SessionChanged: 'Sessionen er opdateret',
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
  if (!notificationAffectsRole(notification, role)) return false;
  if (!sessionId || !notification.sessionId) return true;
  return notification.sessionId.toLowerCase() === sessionId.toLowerCase()
    && (generation == null || notification.generation == null || notification.generation === generation
      || notification.eventType === 'SessionChanged' && notification.generation > generation);
}

export function notificationAffectsRole(notification: Notification, role: NotificationRole): boolean {
  return !notification.recipientRole || notification.recipientRole === role;
}

export enum SessionRefresh {
  None = 0,
  Sessions = 1,
  Session = 2,
  Queue = 4,
  Requests = 8,
  Conversation = 16,
  Faqs = 32,
  Archives = 64,
  Archive = 128
}

export interface SessionRefreshContext {
  role: NotificationRole;
  sessionId: string;
  generation?: number;
  selectedRequestId?: string;
  conversationStatus?: string;
}

export function visibleSessionRefresh(context: SessionRefreshContext): SessionRefresh {
  if (!context.sessionId) return SessionRefresh.Sessions;
  return SessionRefresh.Session | SessionRefresh.Queue | SessionRefresh.Faqs | SessionRefresh.Archives
    | SessionRefresh.Archive | SessionRefresh.Conversation
    | (context.role === 'students' ? SessionRefresh.Requests : SessionRefresh.None);
}

const waitingCountChanges = new Set(['Created', 'StudentMessageAdded', 'Started', 'Answered', 'Cancelled',
  'ReturnedToQueue', 'FaqCreated', 'FaqLinked', 'FaqRejected', 'FaqAnswered', 'FaqSuggested']);
const queueChanges = new Set([...waitingCountChanges, 'Reserved', 'Completed']);
const lifecycleChanges = new Set(['Cleared', 'Archived', 'Deleted']);
const eventChanges: Partial<Record<NotificationEventType, string>> = {
  HelpRequestCreated: 'Created', StudentMessageAdded: 'StudentMessageAdded', HelpRequestStarted: 'Started',
  HelpRequestAnswered: 'Answered', FaqCreated: 'FaqCreated', FaqAnswered: 'FaqSuggested'
};

export function notificationRefreshParts(notification: Notification, context: SessionRefreshContext): SessionRefresh {
  if (!notificationAffectsRole(notification, context.role)) return SessionRefresh.None;
  const change = notification.change ?? eventChanges[notification.eventType ?? 'Unknown'];
  if (!notification.sessionId) return visibleSessionRefresh(context);
  if (!context.sessionId) {
    if (lifecycleChanges.has(change ?? '') || change == null || !queueChanges.has(change)
      && !['FaqAccepted', 'FaqCancelled', 'FaqSuggested', 'ArchivedFaqAnswered'].includes(change)) return SessionRefresh.Sessions;
    return context.role === 'teachers' && waitingCountChanges.has(change) ? SessionRefresh.Sessions : SessionRefresh.None;
  }
  if (notification.sessionId.toLowerCase() !== context.sessionId.toLowerCase()) return SessionRefresh.None;
  if (context.generation != null && notification.generation != null && notification.generation > context.generation)
    return visibleSessionRefresh(context);
  if (change === 'ArchivedFaqAnswered') return SessionRefresh.Archives | SessionRefresh.Archive;
  if (!notificationAffectsSession(notification, context.role, context.sessionId, context.generation)) return SessionRefresh.None;
  if (lifecycleChanges.has(change ?? '')) return visibleSessionRefresh(context);
  if (!change || !queueChanges.has(change) && !['FaqAccepted', 'FaqCancelled', 'FaqSuggested'].includes(change))
    return visibleSessionRefresh(context);

  const student = context.role === 'students';
  const ownRequest = student && !!notification.helpRequestId;
  const selected = !!notification.helpRequestId && notification.helpRequestId.toLowerCase() === context.selectedRequestId?.toLowerCase();
  let parts = queueChanges.has(change) && (change !== 'Reserved' || !student) ? SessionRefresh.Queue : SessionRefresh.None;
  if (ownRequest && change !== 'Reserved') parts |= SessionRefresh.Requests;
  if (ownRequest && ['FaqAccepted', 'FaqCancelled'].includes(change)) parts |= SessionRefresh.Queue;
  if (selected) parts |= SessionRefresh.Conversation;
  if (!student && !notification.helpRequestId && change === 'FaqAnswered'
    && ['Pending', 'AwaitingFaq', 'AwaitingConfirmation'].includes(context.conversationStatus ?? ''))
    parts |= SessionRefresh.Conversation;
  if (['FaqAnswered', 'FaqSuggested'].includes(change) || !student && ['FaqCreated', 'FaqLinked'].includes(change))
    parts |= SessionRefresh.Faqs;
  return parts;
}