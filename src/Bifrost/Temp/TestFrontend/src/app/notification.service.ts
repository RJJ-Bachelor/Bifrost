import { Injectable, computed, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { Subject } from 'rxjs';

export interface Notification {
  notificationId: string;
  userId: string;
  message: string;
  createdAt: string;
  sessionId?: string | null;
  helpRequestId?: string | null;
}

@Injectable({ providedIn: 'root' })
export class NotificationService {
  private connection?: HubConnection;
  private retry?: ReturnType<typeof setTimeout>;
  private readonly seen = new Set<string>();
  private readonly pending = signal<Notification[]>([]);
  readonly current = computed(() => this.pending()[0] ?? null);
  readonly reconnected = new Subject<void>();
  readonly received = new Subject<Notification>();

  async connect(role: 'students' | 'teachers') {
    const path = `/api/${role}/notifications`;
    if (this.connection?.baseUrl.endsWith(path)) return;
    await this.stop();
    const connection = new HubConnectionBuilder().withUrl(path, { withCredentials: true })
      .withAutomaticReconnect().configureLogging(LogLevel.Warning).build();
    this.connection = connection;
    connection.on('NotificationReceived', (notification: Notification) => {
      if (this.connection !== connection || this.seen.has(notification.notificationId)) return;
      this.seen.add(notification.notificationId);
      if (this.seen.size > 200) this.seen.delete(this.seen.values().next().value!);
      this.pending.update(messages => [...messages, notification]);
      this.received.next(notification);
    });
    connection.onreconnected(() => this.reconnected.next());
    connection.onclose(() => this.scheduleRetry(connection));
    await this.start(connection);
  }

  dismiss() { this.pending.update(messages => messages.slice(1)); }

  async stop(role?: 'students' | 'teachers') {
    if (role && !this.connection?.baseUrl.endsWith(`/api/${role}/notifications`)) return;
    clearTimeout(this.retry);
    const previous = this.connection;
    this.connection = undefined;
    this.pending.set([]);
    await previous?.stop();
  }

  private async start(connection: HubConnection) {
    if (this.connection !== connection) return;
    try {
      await connection.start();
      if (this.connection !== connection) { await connection.stop(); return; }
      this.reconnected.next();
    } catch (error) {
      console.warn('Notifikationsforbindelsen kunne ikke oprettes. Prøver igen.', error);
      this.scheduleRetry(connection);
    }
  }

  private scheduleRetry(connection: HubConnection) {
    if (this.connection !== connection) return;
    clearTimeout(this.retry);
    this.retry = setTimeout(() => {
      if (connection.state === HubConnectionState.Disconnected) void this.start(connection);
    }, 5000);
  }
}
