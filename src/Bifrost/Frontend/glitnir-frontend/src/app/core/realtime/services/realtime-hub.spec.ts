import { TestBed } from '@angular/core/testing';
import { HubConnection, HubConnectionState } from '@microsoft/signalr';
import { NotificationEventType, type NotificationMessage } from '../notification.model';
import { RealtimeHub } from './realtime-hub';

interface FakeHubConnection {
  handlers: Map<string, (...args: unknown[]) => void>;
  start: ReturnType<typeof vi.fn>;
  stop: ReturnType<typeof vi.fn>;
  on: ReturnType<typeof vi.fn>;
  onreconnecting: ReturnType<typeof vi.fn>;
  onreconnected: ReturnType<typeof vi.fn>;
  onclose: ReturnType<typeof vi.fn>;
}

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
  let fakeConnections: FakeHubConnection[];
  let fakeUrls: string[];
  let failNextStart: boolean;
  let failNextStop: boolean;
  let pendingStart: Promise<void> | null;

  function buildFakeConnection(): FakeHubConnection {
    const handlers = new Map<string, (...args: unknown[]) => void>();
    return {
      handlers,
      start: vi.fn(async () => {
        if (failNextStart) {
          failNextStart = false;
          throw new Error('start failed');
        }
        if (pendingStart) {
          await pendingStart;
        }
      }),
      stop: vi.fn(async () => {
        if (failNextStop) {
          failNextStop = false;
          throw new Error('stop failed');
        }
      }),
      on: vi.fn((event: string, cb: (...args: unknown[]) => void) => {
        handlers.set(event, cb);
      }),
      onreconnecting: vi.fn(),
      onreconnected: vi.fn(),
      onclose: vi.fn((cb: () => void) => {
        handlers.set('__close', cb);
      }),
    };
  }

  beforeEach(() => {
    fakeConnections = [];
    fakeUrls = [];
    failNextStart = false;
    failNextStop = false;
    pendingStart = null;

    TestBed.configureTestingModule({});
    hub = TestBed.inject(RealtimeHub);

    vi.spyOn(hub as unknown as { createConnection: (path: string) => HubConnection }, 'createConnection')
      .mockImplementation((path: string) => {
        fakeUrls.push(path);
        const connection = buildFakeConnection();
        fakeConnections.push(connection);
        return connection as unknown as HubConnection;
      });
  });

  afterEach(async () => {
    pendingStart = null;
    await hub.disconnect();
  });

  describe('connection lifecycle', () => {
    it('starts disconnected without opening a connection', () => {
      expect(hub.status()).toBe(HubConnectionState.Disconnected);
      expect(fakeConnections).toHaveLength(0);
    });

    it('connects to the student hub and moves to Connected', async () => {
      await hub.connect('student', 'user-a');

      expect(fakeUrls).toEqual(['/api/students/notifications']);
      expect(fakeConnections).toHaveLength(1);
      expect(fakeConnections[0].start).toHaveBeenCalledTimes(1);
      expect(hub.status()).toBe(HubConnectionState.Connected);
    });

    it('connects to the teacher hub for the teacher role', async () => {
      await hub.connect('teacher', 'user-a');

      expect(fakeUrls).toEqual(['/api/teachers/notifications']);
    });

    it('keeps a single connection when the same role is requested twice', async () => {
      await hub.connect('student', 'user-a');
      await hub.connect('student', 'user-a');

      expect(fakeConnections).toHaveLength(1);
      expect(fakeConnections[0].stop).not.toHaveBeenCalled();
    });

    it('replaces the connection when the role changes', async () => {
      await hub.connect('student', 'user-a');
      await hub.connect('teacher', 'user-a');

      expect(fakeConnections).toHaveLength(2);
      expect(fakeConnections[0].stop).toHaveBeenCalledTimes(1);
      expect(fakeUrls).toEqual([
        '/api/students/notifications',
        '/api/teachers/notifications',
      ]);
      expect(hub.status()).toBe(HubConnectionState.Connected);
    });

    it('replaces the connection when the userId changes but the role stays the same', async () => {
      await hub.connect('student', 'user-a');
      await hub.connect('student', 'user-b');

      expect(fakeConnections).toHaveLength(2);
      expect(fakeConnections[0].stop).toHaveBeenCalledTimes(1);
      expect(hub.status()).toBe(HubConnectionState.Connected);
    });

    it('disconnect() stops the connection and resets status', async () => {
      await hub.connect('student', 'user-a');

      await hub.disconnect();

      expect(fakeConnections[0].stop).toHaveBeenCalledTimes(1);
      expect(hub.status()).toBe(HubConnectionState.Disconnected);
    });

    it('reconnects after disconnect() even for the same role and user', async () => {
      await hub.connect('student', 'user-a');
      await hub.disconnect();
      await hub.connect('student', 'user-a');

      expect(fakeConnections).toHaveLength(2);
      expect(fakeConnections[0].stop).toHaveBeenCalledTimes(1);
      expect(hub.status()).toBe(HubConnectionState.Connected);
    });
  });

  describe('notifications', () => {
    it('forwards NotificationReceived events to notifications$', async () => {
      await hub.connect('student', 'user-a');
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
      failNextStart = true;

      await hub.connect('student', 'user-a');

      expect(hub.status()).toBe(HubConnectionState.Disconnected);
    });

    it('retries the same role after a failed start', async () => {
      failNextStart = true;
      await hub.connect('student', 'user-a');
      expect(fakeConnections).toHaveLength(1);

      await hub.connect('student', 'user-a');

      expect(fakeConnections).toHaveLength(2);
      expect(hub.status()).toBe(HubConnectionState.Connected);
    });

    it('reports Disconnected when the hub closes and allows reconnecting', async () => {
      await hub.connect('student', 'user-a');
      fakeConnections[0].handlers.get('__close')?.();
      expect(hub.status()).toBe(HubConnectionState.Disconnected);

      await hub.connect('student', 'user-a');

      expect(fakeConnections).toHaveLength(2);
      expect(hub.status()).toBe(HubConnectionState.Connected);
    });

    it('ignores close events from superseded connections', async () => {
      await hub.connect('student', 'user-a');
      const first = fakeConnections[0];

      await hub.connect('teacher', 'user-a');
      expect(hub.status()).toBe(HubConnectionState.Connected);

      first.handlers.get('__close')?.();

      expect(hub.status()).toBe(HubConnectionState.Connected);
      expect(fakeConnections).toHaveLength(2);
    });

    it('does not throw when stop() fails during disconnect', async () => {
      await hub.connect('student', 'user-a');
      failNextStop = true;

      await expect(hub.disconnect()).resolves.toBeUndefined();

      expect(hub.status()).toBe(HubConnectionState.Disconnected);
    });

    it('does not set Connected when disconnect() runs during a pending start()', async () => {
      let releaseStart!: () => void;
      pendingStart = new Promise<void>((resolve) => {
        releaseStart = resolve;
      });

      const connectPromise = hub.connect('student', 'user-a');
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
