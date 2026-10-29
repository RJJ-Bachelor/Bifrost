import { TestBed } from '@angular/core/testing';
import { HubConnectionState } from '@microsoft/signalr';
import { NotificationEventType, type NotificationMessage } from '../notification.model';
import { RealtimeHub } from './realtime-hub';

interface FakeHubConnection {
  handlers: Map<string, (...args: unknown[]) => void>;
  start: ReturnType<typeof vi.fn>;
  stop: ReturnType<typeof vi.fn>;
}

const { fakeConnections, fakeUrls, control } = vi.hoisted(() => ({
  fakeConnections: [] as FakeHubConnection[],
  fakeUrls: [] as string[],
  control: {
    failNextStart: false,
    failNextStop: false,
    pendingStart: null as Promise<void> | null,
  },
}));

vi.mock('@microsoft/signalr', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@microsoft/signalr')>();

  class FakeConnection implements FakeHubConnection {
    handlers = new Map<string, (...args: unknown[]) => void>();
    start = vi.fn(async () => {
      if (control.failNextStart) {
        control.failNextStart = false;
        throw new Error('start failed');
      }
      if (control.pendingStart) {
        await control.pendingStart;
      }
    });
    stop = vi.fn(async () => {
      if (control.failNextStop) {
        control.failNextStop = false;
        throw new Error('stop failed');
      }
    });
    on = vi.fn((event: string, cb: (...args: unknown[]) => void) => {
      this.handlers.set(event, cb);
    });
    onreconnecting = vi.fn((cb: () => void) => {
      this.handlers.set('__reconnecting', cb);
    });
    onreconnected = vi.fn((cb: () => void) => {
      this.handlers.set('__reconnected', cb);
    });
    onclose = vi.fn((cb: () => void) => {
      this.handlers.set('__close', cb);
    });
  }

  class FakeBuilder {
    withUrl(url: string) {
      fakeUrls.push(url);
      return this;
    }
    withAutomaticReconnect() {
      return this;
    }
    configureLogging() {
      return this;
    }
    build() {
      const connection = new FakeConnection();
      fakeConnections.push(connection);
      return connection;
    }
  }

  return { ...actual, HubConnectionBuilder: FakeBuilder };
});

function makeNotification(overrides: Partial<NotificationMessage> = {}): NotificationMessage {
  return {
    notificationId: 'n-1',
    userId: 'u-1',
    message: 'Hej',
    createdAt: '2026-01-01T00:00:00Z',
    sessionId: null,
    helpRequestId: null,
    generation: null,
    change: null,
    status: null,
    eventType: NotificationEventType.HelpRequestCreated,
    deliveryType: 'RealtimeOnly',
    ...overrides,
  };
}

describe('RealtimeHub', () => {
  let hub: RealtimeHub;

  beforeEach(() => {
    fakeConnections.length = 0;
    fakeUrls.length = 0;
    control.failNextStart = false;
    control.failNextStop = false;
    control.pendingStart = null;
    TestBed.configureTestingModule({});
    hub = TestBed.inject(RealtimeHub);
  });

  afterEach(async () => {
    control.pendingStart = null;
    await hub.disconnect();
  });

  describe('connection lifecycle', () => {
    it('starts disconnected without opening a connection', () => {
      expect(hub.status()).toBe(HubConnectionState.Disconnected);
      expect(fakeConnections).toHaveLength(0);
    });

    it('connects to the student hub and moves to Connected', async () => {
      await hub.connect('student');

      expect(fakeUrls).toEqual(['/api/students/notifications']);
      expect(fakeConnections).toHaveLength(1);
      expect(fakeConnections[0].start).toHaveBeenCalledTimes(1);
      expect(hub.status()).toBe(HubConnectionState.Connected);
    });

    it('connects to the teacher hub for the teacher role', async () => {
      await hub.connect('teacher');

      expect(fakeUrls).toEqual(['/api/teachers/notifications']);
    });

    it('keeps a single connection when the same role is requested twice', async () => {
      await hub.connect('student');
      await hub.connect('student');

      expect(fakeConnections).toHaveLength(1);
      expect(fakeConnections[0].stop).not.toHaveBeenCalled();
    });

    it('replaces the connection when the role changes', async () => {
      await hub.connect('student');
      await hub.connect('teacher');

      expect(fakeConnections).toHaveLength(2);
      expect(fakeConnections[0].stop).toHaveBeenCalledTimes(1);
      expect(fakeUrls).toEqual([
        '/api/students/notifications',
        '/api/teachers/notifications',
      ]);
      expect(hub.status()).toBe(HubConnectionState.Connected);
    });

    it('disconnect() stops the connection and resets status', async () => {
      await hub.connect('student');

      await hub.disconnect();

      expect(fakeConnections[0].stop).toHaveBeenCalledTimes(1);
      expect(hub.status()).toBe(HubConnectionState.Disconnected);
    });
  });

  describe('notifications', () => {
    it('forwards NotificationReceived events to notifications$', async () => {
      await hub.connect('student');
      const received: NotificationMessage[] = [];
      const subscription = hub.notifications$.subscribe((n) => received.push(n));

      const notification = makeNotification();
      fakeConnections[0].handlers.get('NotificationReceived')?.(notification);

      subscription.unsubscribe();
      expect(received).toEqual([notification]);
    });
  });

  describe('failure handling', () => {
    it('reports Disconnected when start() fails', async () => {
      control.failNextStart = true;

      await hub.connect('student');

      expect(hub.status()).toBe(HubConnectionState.Disconnected);
    });

    it('retries the same role after a failed start', async () => {
      control.failNextStart = true;
      await hub.connect('student');
      expect(fakeConnections).toHaveLength(1);

      await hub.connect('student');

      expect(fakeConnections).toHaveLength(2);
      expect(hub.status()).toBe(HubConnectionState.Connected);
    });

    it('reports Disconnected when the hub closes and allows reconnecting', async () => {
      await hub.connect('student');
      fakeConnections[0].handlers.get('__close')?.();
      expect(hub.status()).toBe(HubConnectionState.Disconnected);

      await hub.connect('student');

      expect(fakeConnections).toHaveLength(2);
      expect(hub.status()).toBe(HubConnectionState.Connected);
    });

    it('ignores close events from superseded connections', async () => {
      await hub.connect('student');
      const first = fakeConnections[0];

      await hub.connect('teacher');
      expect(hub.status()).toBe(HubConnectionState.Connected);

      first.handlers.get('__close')?.();

      expect(hub.status()).toBe(HubConnectionState.Connected);
      expect(fakeConnections).toHaveLength(2);
    });

    it('does not throw when stop() fails during disconnect', async () => {
      await hub.connect('student');
      control.failNextStop = true;

      await expect(hub.disconnect()).resolves.toBeUndefined();

      expect(hub.status()).toBe(HubConnectionState.Disconnected);
    });

    it('does not set Connected when disconnect() runs during a pending start()', async () => {
      let releaseStart!: () => void;
      control.pendingStart = new Promise<void>((resolve) => {
        releaseStart = resolve;
      });

      const connectPromise = hub.connect('student');
      await vi.waitFor(() => {
        expect(fakeConnections).toHaveLength(1);
        expect(fakeConnections[0].start).toHaveBeenCalled();
      });

      const disconnectPromise = hub.disconnect();
      releaseStart();
      await connectPromise;
      await disconnectPromise;

      expect(hub.status()).toBe(HubConnectionState.Disconnected);
    });
  });
});
