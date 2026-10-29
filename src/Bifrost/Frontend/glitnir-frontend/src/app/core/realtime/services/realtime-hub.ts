import { Service, inject, signal } from '@angular/core';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { AuthStore } from '../../auth/services/auth-store';
import { UserRole } from '../../auth/auth.model';
import { NotificationMessage } from '../notification.model';

@Service()
export class RealtimeHub {
  private readonly authStore = inject(AuthStore);
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
    connection.onclose(() => this._status.set(HubConnectionState.Disconnected));

    this.connection = connection;
    this._status.set(HubConnectionState.Connecting);

    try {
      await connection.start();
      this._status.set(HubConnectionState.Connected);
    } catch {
      this._status.set(HubConnectionState.Disconnected);
    }
  }

  async disconnect(): Promise<void> {
    if (this.connection) {
      await this.connection.stop();
      this.connection = null;
      this.connectionRole = null;
      this._status.set(HubConnectionState.Disconnected);
    }
  }
}
