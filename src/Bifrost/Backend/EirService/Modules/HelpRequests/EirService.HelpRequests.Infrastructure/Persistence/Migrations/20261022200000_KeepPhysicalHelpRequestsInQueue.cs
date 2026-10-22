using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EirService.HelpRequests.Infrastructure.Persistence.Migrations;

[DbContext(typeof(EirRequestDbContext))]
[Migration("20261022200000_KeepPhysicalHelpRequestsInQueue")]
public sealed class KeepPhysicalHelpRequestsInQueue : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Older versions paused physical help while a shared FAQ was being answered.
        // Restore that help without changing its queue position or conversation history.
        migrationBuilder.Sql("""
            UPDATE helprequests."HelpRequest"
            SET "Status" = 'Pending', "FaqAnswerId" = NULL, "AssignedTeacherId" = NULL,
                "StartedAt" = NULL, "ClosedAt" = NULL
            WHERE "StudentId" IS NOT NULL AND "HelpType" = 'Physical'
                AND "Status" IN ('AwaitingFaq', 'AwaitingConfirmation');
            """);

        // Unpublished FAQ notices from the old behavior must not interrupt physical help.
        // Published notices and notifications about the physical help itself remain intact.
        migrationBuilder.Sql("""
            DELETE FROM helprequests.outbox AS notification
            USING helprequests."HelpRequest" AS request
            WHERE notification."PublishedAtUtc" IS NULL
                AND notification."ExchangeName" = 'notifications'
                AND notification."RoutingKey" = 'gna-notification-requested'
                AND notification."Payload"::jsonb ->> 'helpRequestId' = request."Id"::text
                AND notification."Payload"::jsonb ->> 'userId' = request."StudentId"
                AND request."StudentId" IS NOT NULL AND request."HelpType" = 'Physical'
                AND (notification."Payload"::jsonb ->> 'change' = 'FaqSuggested'
                    OR notification."Payload"::jsonb ->> 'eventType' = 'FaqAnswered');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The old FAQ state and suppressed notices cannot be safely reconstructed.
    }
}
