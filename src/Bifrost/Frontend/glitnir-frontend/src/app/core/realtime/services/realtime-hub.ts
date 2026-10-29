import { Service, signal } from '@angular/core';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { UserRole } from '../../auth/auth.model';
import { NotificationMessage } from '../notification.model';

@Service()
export class RealtimeHub {
  private connection: HubConnection | null = null;
  private connectionRole: UserRole | null = null;

  private readonly _status = signal<HubConnectionState>(HubConnectionState.Disconnected);
  readonly status = this._status.asReadonly();

  private readonly _notifications = new Subject<NotificationMessage>();
  readonly notifications$ = this._notifications.asObservable();

  async connect(role: UserRole): Promise<void> {
    if (this.connection && this.connectionRole === role) {
      return;
    }

    await this.disconnect();

    const path = role === 'teacher' ? '/api/teachers/notifications' : '/api/students/notifications';
    this.connectionRole = role;

    const connection = new HubConnectionBuilder()
      .withUrl(path, { withCredentials: true })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on('NotificationReceived', (notification: NotificationMessage) => {
      this._notifications.next(notification);
    });

    connection.onreconnecting(() => this._status.set(HubConnectionState.Reconnecting));
    connection.onreconnected(() => this._status.set(HubConnectionState.Connected));
    connection.onclose(() => {
      // Ignore stale close events from a superseded connection.
      if (this.connection === connection) {
        this.connection = null;
        this.connectionRole = null;
        this._status.set(HubConnectionState.Disconnected);
      }
    });

    this.connection = connection;
    this._status.set(HubConnectionState.Connecting);

    try {
      await connection.start();
      if (this.connection === connection) {
        this._status.set(HubConnectionState.Connected);
      }
    } catch {
      if (this.connection === connection) {
        this.connection = null;
        this.connectionRole = null;
        this._status.set(HubConnectionState.Disconnected);
      }
    }
  }

  async disconnect(): Promise<void> {
    const connection = this.connection;
    if (connection === null) return;

    this.connection = null;
    this.connectionRole = null;
    this._status.set(HubConnectionState.Disconnected);

    try {
      await connection.stop();
    } catch {
      // stop() may throw; state is already reset above.
    }
  }
}
