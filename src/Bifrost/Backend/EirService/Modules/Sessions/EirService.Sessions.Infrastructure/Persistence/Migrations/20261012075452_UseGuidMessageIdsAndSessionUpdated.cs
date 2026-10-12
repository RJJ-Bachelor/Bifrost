using Microsoft.EntityFrameworkCore.Migrations;
using Shared.Infrastructure.Persistence.Migrations;

#nullable disable

namespace EirService.Sessions.Infrastructure.Persistence.Migrations;

public partial class UseGuidMessageIdsAndSessionUpdated : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(GuidIdMigration.ToGuid("sessions", "outbox", "MessageId"));
        // Upgrade Clear events already waiting in the outbox to the new shared contract.
        migrationBuilder.Sql("""
            UPDATE sessions.outbox
            SET "MessageType" = 'Shared.Application.Messaging.Contracts.SessionUpdatedIntegrationEvent',
                "RoutingKey" = 'session.updated',
                "Payload" = (("Payload"::jsonb - 'currentGeneration' - 'clearedAtUtc') || jsonb_build_object(
                    'generation', "Payload"::jsonb->'currentGeneration',
                    'eventType', 'Cleared',
                    'updatedAtUtc', "Payload"::jsonb->'clearedAtUtc'))::text
            WHERE "MessageType" = 'Shared.Application.Messaging.Contracts.SessionClearedIntegrationEvent'
                AND "RoutingKey" = 'session.cleared';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE sessions.outbox
            SET "MessageType" = 'Shared.Application.Messaging.Contracts.SessionClearedIntegrationEvent',
                "RoutingKey" = 'session.cleared',
                "Payload" = (("Payload"::jsonb - 'generation' - 'eventType' - 'updatedAtUtc') || jsonb_build_object(
                    'currentGeneration', "Payload"::jsonb->'generation',
                    'clearedAtUtc', "Payload"::jsonb->'updatedAtUtc'))::text
            WHERE "MessageType" = 'Shared.Application.Messaging.Contracts.SessionUpdatedIntegrationEvent'
                AND "Payload"::jsonb->>'eventType' = 'Cleared';
            """);
        migrationBuilder.Sql(GuidIdMigration.ToString("sessions", "outbox", "MessageId", 200));
    }
}
