using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EirService.Sessions.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionClearedIntegrationOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "outbox",
                schema: "sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MessageType = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    ExchangeName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    RoutingKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastAttemptAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TraceParent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    TraceState = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_PublishedAtUtc_CreatedAtUtc",
                schema: "sessions",
                table: "outbox",
                columns: new[] { "PublishedAtUtc", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox",
                schema: "sessions");
        }
    }
}
