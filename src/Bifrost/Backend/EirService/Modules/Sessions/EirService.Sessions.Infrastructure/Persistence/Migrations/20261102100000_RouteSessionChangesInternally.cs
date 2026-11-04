using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EirService.Sessions.Infrastructure.Persistence.Migrations;

[DbContext(typeof(EirSessionDbContext))]
[Migration("20261102100000_RouteSessionChangesInternally")]
public sealed class RouteSessionChangesInternally : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Replay historical lifecycle work, including messages already in the retired Eir broker queue.
        // Archive cleanup is idempotent. External events remain available for Mimir.
        migrationBuilder.Sql("""
            INSERT INTO sessions.outbox (
                "Id", "MessageId", "MessageType", "Payload", "ExchangeName", "RoutingKey",
                "CreatedAtUtc", "PublishedAtUtc", "LastAttemptAtUtc", "TraceParent", "TraceState")
            SELECT gen_random_uuid(), source."MessageId",
                'EirService.Contracts.HelpRequests.HandleSessionChange.HandleSessionChangeCommand',
                source."Payload", 'bifrost.internal', 'eir.session.changed',
                source."CreatedAtUtc", NULL, NULL, source."TraceParent", source."TraceState"
            FROM sessions.outbox AS source
            WHERE source."ExchangeName" = 'sessions'
                AND source."RoutingKey" = 'session.updated'
                AND source."MessageType" = 'Shared.Application.Messaging.Contracts.SessionUpdatedIntegrationEvent'
                AND NOT EXISTS (
                    SELECT 1 FROM sessions.outbox AS target
                    WHERE target."MessageId" = source."MessageId"
                        AND target."ExchangeName" = 'bifrost.internal'
                        AND target."RoutingKey" = 'eir.session.changed');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Data replay is intentionally irreversible: migrated rows cannot safely be distinguished
        // from commands created after deployment. Leave the durable work intact on rollback.
    }
}
