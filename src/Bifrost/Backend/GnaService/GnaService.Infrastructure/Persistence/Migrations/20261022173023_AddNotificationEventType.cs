using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnaService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationEventType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EventType",
                table: "notifications",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.Sql("""
                UPDATE notifications SET "EventType" = CASE "Change"
                    WHEN 'Created' THEN 'HelpRequestCreated'
                    WHEN 'StudentMessageAdded' THEN 'StudentMessageAdded'
                    WHEN 'Started' THEN 'HelpRequestStarted'
                    WHEN 'Answered' THEN 'HelpRequestAnswered'
                    WHEN 'FaqCreated' THEN 'FaqCreated'
                    WHEN 'FaqSuggested' THEN 'FaqAnswered'
                    ELSE 'Unknown'
                END;

                UPDATE outbox AS delivery
                SET "Payload" = (delivery."Payload"::jsonb || jsonb_build_object(
                    'eventType', notification."EventType", 'generation', notification."Generation",
                    'change', notification."Change", 'status', notification."Status"))::text
                FROM notifications AS notification
                WHERE delivery."MessageId" = notification."Id" AND delivery."PublishedAtUtc" IS NULL
                    AND delivery."RoutingKey" = 'notification.delivery-requested';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EventType",
                table: "notifications");
        }
    }
}
