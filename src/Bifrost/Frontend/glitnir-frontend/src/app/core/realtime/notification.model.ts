export enum NotificationEventType {
  Unknown = 'Unknown',
  HelpRequestCreated = 'HelpRequestCreated',
  StudentMessageAdded = 'StudentMessageAdded',
  HelpRequestStarted = 'HelpRequestStarted',
  HelpRequestAnswered = 'HelpRequestAnswered',
  FaqCreated = 'FaqCreated',
  FaqAnswered = 'FaqAnswered',
  SessionChanged = 'SessionChanged',
}

export type NotificationDeliveryType = 'RealtimeOnly' | 'RealtimeOrPush';

export interface NotificationMessage {
  notificationId: string;
  userId: string;
  message: string;
  createdAt: string;
  sessionId: string | null;
  helpRequestId: string | null;
  generation: number | null;
  change: string | null;
  status: string | null;
  eventType: NotificationEventType;
  deliveryType: NotificationDeliveryType;
}
