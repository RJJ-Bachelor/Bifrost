import { Injectable, computed, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { Notification, notificationTarget, notificationTitle } from './notification.model';

@Injectable({ providedIn: 'root' })
export class NotificationService {
  private connection?: HubConnection;
  private connectionUserId?: string;
  private connectionVersion = 0;
  private retry?: ReturnType<typeof setTimeout>;
  private readonly seen = new Set<string>();
  private readonly pending = signal<Notification[]>([]);
  readonly current = computed(() => this.pending()[0] ?? null);
  readonly title = computed(() => notificationTitle(this.current()));
  readonly target = computed(() => notificationTarget(this.current()));
  readonly count = computed(() => this.pending().length);
  readonly reconnected = new Subject<void>();
  readonly received = new Subject<Notification>();

  async connect(role: 'students' | 'teachers', userId: string) {
    const path = `/api/${role}/notifications`;
    if (this.connection?.baseUrl.endsWith(path) && this.connectionUserId === userId) return;
    const version = ++this.connectionVersion;
    await this.clearConnection();
    if (version !== this.connectionVersion) return;
    const connection = new HubConnectionBuilder().withUrl(path, { withCredentials: true })
      .withAutomaticReconnect().configureLogging(LogLevel.Warning).build();
    this.connection = connection;
    this.connectionUserId = userId;
    connection.on('NotificationReceived', (notification: Notification) => {
      const key = `${role}:${notification.userId}:${notification.notificationId}`;
      if (this.connection !== connection || notification.userId !== userId || this.seen.has(key)) return;
      this.seen.add(key);
      if (this.seen.size > 200) this.seen.delete(this.seen.values().next().value!);
      const received = { ...notification, recipientRole: role };
      this.pending.update(messages => [...messages, received]);
      this.received.next(received);
    });
    connection.onreconnected(() => {
      if (this.connection === connection) this.reconnected.next();
    });
    connection.onclose(() => this.scheduleRetry(connection));
    await this.start(connection);
  }

  dismiss() { this.pending.update(messages => messages.slice(1)); }

  async stop(role?: 'students' | 'teachers') {
    if (role && !this.connection?.baseUrl.endsWith(`/api/${role}/notifications`)) return;
    this.connectionVersion++;
    await this.clearConnection();
  }

  private async clearConnection() {
    clearTimeout(this.retry);
    const previous = this.connection;
    this.connection = undefined;
    this.connectionUserId = undefined;
    this.pending.set([]);
    this.seen.clear();
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
