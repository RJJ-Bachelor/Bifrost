using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EirService.HelpRequests.Infrastructure.Persistence.Migrations;

[DbContext(typeof(EirRequestDbContext))]
[Migration("20261027090000_DistinguishHelpRequestCompletion")]
public sealed class DistinguishHelpRequestCompletion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // A student's accepted FAQ takes precedence over the original help type.
        // The shared FAQ queue item itself is answered digitally and has no student.
        migrationBuilder.Sql("""
            UPDATE helprequests."HelpRequest"
            SET "Status" = CASE
                WHEN "StudentId" IS NOT NULL AND "FaqAnswerId" IS NOT NULL THEN 'CompletedByFaq'
                WHEN "HelpType" = 'Physical' THEN 'CompletedByPhysical'
                ELSE 'CompletedByDigital'
            END
            WHERE "Status" = 'Completed';

            UPDATE helprequests."ArchivedConversation"
            SET "Data" = jsonb_set("Data", '{status}', to_jsonb(CASE
                WHEN "Data" ->> 'faqAnswerId' IS NOT NULL THEN 'CompletedByFaq'
                WHEN "Data" ->> 'helpType' = 'Physical' THEN 'CompletedByPhysical'
                ELSE 'CompletedByDigital'
            END))
            WHERE "Data" ->> 'status' = 'Completed';
            """);

        // Use the event's change, since the live request may already have reopened
        // or been removed. Already published messages remain historical records.
        migrationBuilder.Sql("""
            UPDATE helprequests.outbox
            SET "Payload" = jsonb_set("Payload"::jsonb, '{status}', to_jsonb(CASE
                WHEN "Payload"::jsonb ->> 'change' = 'FaqAccepted' THEN 'CompletedByFaq'
                WHEN "Payload"::jsonb ->> 'change' = 'Completed' THEN 'CompletedByPhysical'
                ELSE 'CompletedByDigital'
            END))::text
            WHERE "PublishedAtUtc" IS NULL AND "Payload"::jsonb ->> 'status' = 'Completed'
                AND "Payload"::jsonb ->> 'change' IN ('FaqAccepted', 'Completed', 'Answered')
                AND (("ExchangeName" = 'requests' AND "RoutingKey" = 'request.updated')
                    OR ("ExchangeName" = 'notifications' AND "RoutingKey" = 'gna-notification-requested'));
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE helprequests."HelpRequest" SET "Status" = 'Completed'
            WHERE "Status" IN ('CompletedByFaq', 'CompletedByDigital', 'CompletedByPhysical');

            UPDATE helprequests."ArchivedConversation"
            SET "Data" = jsonb_set("Data", '{status}', '"Completed"'::jsonb)
            WHERE "Data" ->> 'status' IN ('CompletedByFaq', 'CompletedByDigital', 'CompletedByPhysical');

            UPDATE helprequests.outbox
            SET "Payload" = jsonb_set("Payload"::jsonb, '{status}', '"Completed"'::jsonb)::text
            WHERE "PublishedAtUtc" IS NULL
                AND "Payload"::jsonb ->> 'status' IN ('CompletedByFaq', 'CompletedByDigital', 'CompletedByPhysical')
                AND (("ExchangeName" = 'requests' AND "RoutingKey" = 'request.updated')
                    OR ("ExchangeName" = 'notifications' AND "RoutingKey" = 'gna-notification-requested'));
            """);
    }
}
