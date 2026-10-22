using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EirService.HelpRequests.Infrastructure.Persistence.Migrations;

[DbContext(typeof(EirRequestDbContext))]
[Migration("20261022220000_HideRejectedFaqMessages")]
public sealed class HideRejectedFaqMessages : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsRejected",
            schema: "helprequests",
            table: "HelpRequestMessage",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        // Rejection used to clear FaqAnswerId without recording the decision on the
        // messages. Published and pending request.updated events retain that decision.
        // Use the episode that was current when it happened, rather than hiding all
        // FAQ history: an accepted answer can legitimately precede a later follow-up.
        // If no rejection event remains, preserve the history instead of guessing.
        migrationBuilder.Sql("""
            WITH faq_episodes AS (
                SELECT question."HelpRequestId", question."SequenceNumber", question."CreatedAt",
                    LEAD(question."SequenceNumber") OVER (
                        PARTITION BY question."HelpRequestId" ORDER BY question."SequenceNumber"
                    ) AS next_question_sequence
                FROM helprequests."HelpRequestMessage" AS question
                WHERE question."MessageType" = 'FaqQuestion'
            ), rejection_events AS (
                SELECT event."Payload"::jsonb ->> 'helpRequestId' AS request_id,
                    (event."Payload"::jsonb ->> 'occurredAt')::timestamptz AS rejected_at
                FROM helprequests.outbox AS event
                WHERE event."ExchangeName" = 'requests' AND event."RoutingKey" = 'request.updated'
                    AND event."Payload"::jsonb ->> 'change' = 'FaqRejected'
            ), rejected_episodes AS (
                SELECT DISTINCT request."Id", episode."SequenceNumber", episode.next_question_sequence
                FROM rejection_events AS rejection
                JOIN helprequests."HelpRequest" AS request ON request."Id"::text = rejection.request_id
                JOIN LATERAL (
                    SELECT candidate.*
                    FROM faq_episodes AS candidate
                    WHERE candidate."HelpRequestId" = request."Id"
                        AND candidate."CreatedAt" <= rejection.rejected_at
                    ORDER BY candidate."SequenceNumber" DESC
                    LIMIT 1
                ) AS episode ON TRUE
                WHERE request."StudentId" IS NOT NULL
                    AND NOT (request."FaqAnswerId" IS NOT NULL AND episode.next_question_sequence IS NULL)
                    AND EXISTS (
                        SELECT 1 FROM helprequests."HelpRequestMessage" AS answer
                        WHERE answer."HelpRequestId" = request."Id" AND answer."MessageType" = 'FaqAnswer'
                            AND answer."SequenceNumber" > episode."SequenceNumber"
                            AND (episode.next_question_sequence IS NULL
                                OR answer."SequenceNumber" < episode.next_question_sequence)
                            AND answer."CreatedAt" <= rejection.rejected_at
                    )
            )
            UPDATE helprequests."HelpRequestMessage" AS message
            SET "IsRejected" = TRUE
            FROM rejected_episodes AS rejection
            WHERE message."HelpRequestId" = rejection."Id"
                AND message."SequenceNumber" >= rejection."SequenceNumber"
                AND (rejection.next_question_sequence IS NULL
                    OR message."SequenceNumber" < rejection.next_question_sequence)
                AND message."MessageType" IN ('FaqQuestion', 'FaqAnswer');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn(
        name: "IsRejected", schema: "helprequests", table: "HelpRequestMessage");
}
