import {
  NotificationEventType,
  type NotificationMessage,
} from '../app/core/realtime/notification.model';

export function makeNotification(
  overrides: Partial<NotificationMessage> = {},
): NotificationMessage {
  return {
    notificationId: 'notification-1',
    userId: 'user-1',
    message: '',
    createdAt: '2026-01-01T10:00:00Z',
    sessionId: 'session-1',
    helpRequestId: null,
    generation: null,
    change: null,
    status: null,
    eventType: NotificationEventType.SessionChanged,
    deliveryType: 'RealtimeOnly',
    ...overrides,
  };
}
