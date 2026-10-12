using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EirService.HelpRequests.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MoveHelpRequestsToOwnSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $migration$
                DECLARE
                    primary_key_name text;
                BEGIN
                    SELECT conname INTO primary_key_name
                    FROM pg_constraint
                    WHERE conrelid = 'public.requests'::regclass AND contype = 'p';
                    EXECUTE format('ALTER TABLE public.requests RENAME CONSTRAINT %I TO %I',
                        primary_key_name, 'PK_HelpRequest');
                END $migration$;
                """);

            migrationBuilder.EnsureSchema(
                name: "helprequests");

            migrationBuilder.RenameTable(
                name: "outbox",
                schema: "public",
                newName: "outbox",
                newSchema: "helprequests");

            migrationBuilder.RenameTable(
                name: "requests",
                schema: "public",
                newName: "HelpRequest",
                newSchema: "helprequests");

            migrationBuilder.RenameIndex(
                name: "IX_requests_UserId",
                schema: "helprequests",
                table: "HelpRequest",
                newName: "IX_HelpRequest_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_requests_SessionId_Generation_UserId",
                schema: "helprequests",
                table: "HelpRequest",
                newName: "IX_HelpRequest_SessionId_Generation_UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE helprequests."HelpRequest" RENAME CONSTRAINT "PK_HelpRequest" TO "PK_requests";
                """);

            migrationBuilder.RenameTable(
                name: "outbox",
                schema: "helprequests",
                newName: "outbox",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "HelpRequest",
                schema: "helprequests",
                newName: "requests",
                newSchema: "public");

            migrationBuilder.RenameIndex(
                name: "IX_HelpRequest_UserId",
                schema: "public",
                table: "requests",
                newName: "IX_requests_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_HelpRequest_SessionId_Generation_UserId",
                schema: "public",
                table: "requests",
                newName: "IX_requests_SessionId_Generation_UserId");
        }
    }
}
