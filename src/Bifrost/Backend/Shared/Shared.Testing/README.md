# Shared.Testing

Small test helpers shared by the service and backend integration tests. This
project has no xUnit dependency and is referenced only by test projects.

- `SignalRTestClient.HandshakeAsync` and `ReadAsync` preserve complete SignalR
  records across fragmented WebSocket reads, retain additional records for the
  next read, and ignore keep-alive pings. Each operation has a ten-second deadline
  and also honors the caller's cancellation token. Use one reader at a time per
  socket.
- `PostgresTestDatabase` creates a uniquely named database from an administrator
  connection string. Dispose all hosts, service providers and database contexts
  before disposing it. Cleanup has its own deadline and clears only the fixture's
  connection pool. Application pooling settings remain available to concurrency
  tests.
- `RabbitMqTestBroker` creates a private virtual host and grants access to the
  configured AMQP user. Its management API credentials come from the AMQP URI.
  Dispose application connections first; cleanup deletes only the owned virtual
  host and has an independent fifteen-second deadline.
- `TcpFaultProxy` forwards connections through an ephemeral loopback port.
  `DisconnectAsync` closes existing connections and rejects new ones until
  `Resume` is called. Teardown drains the forwarding tasks.

Tests requiring real servers read `BIFROST_TEST_POSTGRES` and, for broker tests,
`BIFROST_TEST_RABBITMQ`. Use isolated test servers; the PostgreSQL user must be
able to create and drop databases. The existing fact attributes skip these tests
when the required connections are absent.

Scenarios using private RabbitMQ virtual hosts also require
`BIFROST_TEST_RABBITMQ_MANAGEMENT`, for example `http://localhost:15672/`, and an
AMQP user permitted to manage virtual hosts and user permissions. GitHub Actions
provides isolated PostgreSQL and RabbitMQ service containers for these tests.
