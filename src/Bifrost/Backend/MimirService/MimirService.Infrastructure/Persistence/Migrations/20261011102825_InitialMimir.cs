using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimirService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialMimir : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "outbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MessageType = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    ExchangeName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    RoutingKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastAttemptAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TraceParent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    TraceState = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox", x => x.Id);
                });

            // Adopt databases created by the previous EnsureCreated startup.
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS public.requests (
                    "Id" character varying(100) NOT NULL,
                    "Message" character varying(4000) NOT NULL,
                    "ReceivedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_requests" PRIMARY KEY ("Id")
                );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_PublishedAtUtc_CreatedAtUtc",
                table: "outbox",
                columns: new[] { "PublishedAtUtc", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox");

            // Keep the adopted requests table and its existing data.
        }
    }
}
