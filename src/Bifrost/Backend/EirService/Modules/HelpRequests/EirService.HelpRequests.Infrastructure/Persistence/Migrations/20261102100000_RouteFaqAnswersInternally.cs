using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EirService.HelpRequests.Infrastructure.Persistence.Migrations;

[DbContext(typeof(EirRequestDbContext))]
[Migration("20261102100000_RouteFaqAnswersInternally")]
public sealed class RouteFaqAnswersInternally : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Published means accepted by RabbitMQ, not necessarily saved in KnowledgeBase.
        // Replay every historical answer locally; RecordAnswer is idempotent.
        migrationBuilder.Sql("""
            UPDATE helprequests.outbox
            SET "ExchangeName" = 'bifrost.internal',
                "RoutingKey" = 'eir.faq.answered',
                "MessageType" = 'EirService.Contracts.KnowledgeBase.RecordFaqAnswer.RecordFaqAnswerCommand',
                "PublishedAtUtc" = NULL,
                "LastAttemptAtUtc" = NULL
            WHERE "ExchangeName" = 'requests' AND "RoutingKey" = 'faq.answered';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE helprequests.outbox
            SET "ExchangeName" = 'requests',
                "RoutingKey" = 'faq.answered',
                "MessageType" = 'Shared.Application.Messaging.Contracts.FaqAnsweredMessage',
                "PublishedAtUtc" = NULL,
                "LastAttemptAtUtc" = NULL
            WHERE "ExchangeName" = 'bifrost.internal' AND "RoutingKey" = 'eir.faq.answered';
            """);
    }
}
