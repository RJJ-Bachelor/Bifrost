using Microsoft.EntityFrameworkCore.Migrations;

namespace Shared.Infrastructure.Persistence.Messaging;

// Keep this version immutable: migrations in all outbox-owning services use this additive upgrade.
public static class MessageDeliveryMigrationV1
{
    public static void Up(MigrationBuilder migration, string? schema, bool inbox = false)
    {
        migration.AddColumn<int>("SchemaVersion", "outbox", schema: schema, nullable: false, defaultValue: 1);
        migration.AddColumn<DateTimeOffset>("OccurredAtUtc", "outbox", schema: schema, nullable: true);
        migration.AddColumn<Guid>("CorrelationId", "outbox", schema: schema, nullable: true);
        migration.AddColumn<Guid>("CausationId", "outbox", schema: schema, nullable: true);
        migration.AddColumn<int>("FailureCount", "outbox", schema: schema, nullable: false, defaultValue: 0);
        migration.AddColumn<DateTimeOffset>("NextAttemptAtUtc", "outbox", schema: schema, nullable: true);
        migration.AddColumn<DateTimeOffset>("DeadLetteredAtUtc", "outbox", schema: schema, nullable: true);
        migration.AddColumn<string>("LastError", "outbox", schema: schema, maxLength: 2000, nullable: true);
        if (!inbox) return;
        migration.CreateTable("inbox", schema: schema, columns: table => new
        {
            Consumer = table.Column<string>(maxLength: 200, nullable: false),
            MessageType = table.Column<string>(maxLength: 1000, nullable: false),
            MessageId = table.Column<Guid>(nullable: false),
            Envelope = table.Column<string>(nullable: false),
            Attempts = table.Column<int>(nullable: false),
            ReceivedAtUtc = table.Column<DateTimeOffset>(nullable: false),
            ReplayRequestedAtUtc = table.Column<DateTimeOffset>(nullable: true),
            CompletedAtUtc = table.Column<DateTimeOffset>(nullable: true),
            NextAttemptAtUtc = table.Column<DateTimeOffset>(nullable: true),
            DeadLetteredAtUtc = table.Column<DateTimeOffset>(nullable: true),
            LastError = table.Column<string>(maxLength: 2000, nullable: true)
        }, constraints: table => table.PrimaryKey("PK_inbox", row => new { row.Consumer, row.MessageType, row.MessageId }));
        migration.CreateIndex("IX_inbox_DeadLetteredAtUtc", "inbox", "DeadLetteredAtUtc", schema: schema);
    }

    public static void Down(MigrationBuilder migration, string? schema, bool inbox = false)
    {
        if (inbox) migration.DropTable("inbox", schema);
        foreach (var column in new[] { "SchemaVersion", "OccurredAtUtc", "CorrelationId", "CausationId", "FailureCount",
            "NextAttemptAtUtc", "DeadLetteredAtUtc", "LastError" }) migration.DropColumn(column, "outbox", schema);
    }
}
