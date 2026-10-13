using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EirService.HelpRequests.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HelpRequestQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HelpRequest_SessionId_Generation_UserId",
                schema: "helprequests",
                table: "HelpRequest");

            migrationBuilder.DropIndex(
                name: "IX_HelpRequest_UserId",
                schema: "helprequests",
                table: "HelpRequest");

            migrationBuilder.RenameColumn(
                name: "UserId",
                schema: "helprequests",
                table: "HelpRequest",
                newName: "StudentId");

            migrationBuilder.Sql("""
                UPDATE helprequests."HelpRequest"
                SET "StudentId" = COALESCE(NULLIF("StudentId", ''), 'legacy:' || "Id"::text),
                    "SessionId" = COALESCE("SessionId", '00000000-0000-0000-0000-000000000000'::uuid),
                    "Generation" = COALESCE("Generation", 1);
                ALTER TABLE helprequests."HelpRequest" ALTER COLUMN "StudentId" SET NOT NULL;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "SessionId",
                schema: "helprequests",
                table: "HelpRequest",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Generation",
                schema: "helprequests",
                table: "HelpRequest",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClosedAt",
                schema: "helprequests",
                table: "HelpRequest",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                schema: "helprequests",
                table: "HelpRequest",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<Guid>(
                name: "FaqAnswerId",
                schema: "helprequests",
                table: "HelpRequest",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HelpType",
                schema: "helprequests",
                table: "HelpRequest",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "QueuedAt",
                schema: "helprequests",
                table: "HelpRequest",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StartedAt",
                schema: "helprequests",
                table: "HelpRequest",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                schema: "helprequests",
                table: "HelpRequest",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AssignedTeacherId",
                schema: "helprequests",
                table: "HelpRequest",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HelpRequestMessage",
                schema: "helprequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HelpRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AuthorRole = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SequenceNumber = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HelpRequestMessage", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HelpRequestMessage_HelpRequest_HelpRequestId",
                        column: x => x.HelpRequestId,
                        principalSchema: "helprequests",
                        principalTable: "HelpRequest",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Old requests have no lifecycle history. Preserve their question and archive them.
            migrationBuilder.Sql("""
                UPDATE helprequests."HelpRequest" SET "HelpType" = 'Digital', "Status" = 'Cancelled',
                    "CreatedAt" = CURRENT_TIMESTAMP, "QueuedAt" = CURRENT_TIMESTAMP, "ClosedAt" = CURRENT_TIMESTAMP;
                INSERT INTO helprequests."HelpRequestMessage"
                    ("Id", "HelpRequestId", "AuthorId", "AuthorRole", "Text", "CreatedAt", "SequenceNumber")
                SELECT gen_random_uuid(), "Id", "StudentId", 'Student',
                    CASE WHEN btrim("Message") = '' THEN '[Empty legacy message]' ELSE "Message" END,
                    "CreatedAt", 1 FROM helprequests."HelpRequest";
                ALTER TABLE helprequests."HelpRequest" DROP COLUMN "Message";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_HelpRequest_OneOpenPerStudent",
                schema: "helprequests",
                table: "HelpRequest",
                columns: new[] { "SessionId", "Generation", "StudentId" },
                unique: true,
                filter: "\"Status\" IN ('Pending', 'Active', 'AwaitingConfirmation')");

            migrationBuilder.CreateIndex(
                name: "IX_HelpRequest_SessionId_Generation_QueuedAt_Id",
                schema: "helprequests",
                table: "HelpRequest",
                columns: new[] { "SessionId", "Generation", "QueuedAt", "Id" },
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_HelpRequest_StudentId",
                schema: "helprequests",
                table: "HelpRequest",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_HelpRequestMessage_HelpRequestId_SequenceNumber",
                schema: "helprequests",
                table: "HelpRequestMessage",
                columns: new[] { "HelpRequestId", "SequenceNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE helprequests."HelpRequest" ADD COLUMN "Message" varchar(4000) NOT NULL DEFAULT '';
                UPDATE helprequests."HelpRequest" AS r SET "Message" = m."Text"
                FROM helprequests."HelpRequestMessage" AS m WHERE m."HelpRequestId" = r."Id" AND m."SequenceNumber" = 1;
                ALTER TABLE helprequests."HelpRequest" ALTER COLUMN "StudentId" DROP NOT NULL;
                """);
            migrationBuilder.DropTable(
                name: "HelpRequestMessage",
                schema: "helprequests");

            migrationBuilder.DropIndex(
                name: "IX_HelpRequest_OneOpenPerStudent",
                schema: "helprequests",
                table: "HelpRequest");

            migrationBuilder.DropIndex(
                name: "IX_HelpRequest_SessionId_Generation_QueuedAt_Id",
                schema: "helprequests",
                table: "HelpRequest");

            migrationBuilder.DropIndex(
                name: "IX_HelpRequest_StudentId",
                schema: "helprequests",
                table: "HelpRequest");

            migrationBuilder.DropColumn(
                name: "ClosedAt",
                schema: "helprequests",
                table: "HelpRequest");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                schema: "helprequests",
                table: "HelpRequest");

            migrationBuilder.DropColumn(
                name: "FaqAnswerId",
                schema: "helprequests",
                table: "HelpRequest");

            migrationBuilder.DropColumn(
                name: "HelpType",
                schema: "helprequests",
                table: "HelpRequest");

            migrationBuilder.DropColumn(
                name: "QueuedAt",
                schema: "helprequests",
                table: "HelpRequest");

            migrationBuilder.DropColumn(
                name: "StartedAt",
                schema: "helprequests",
                table: "HelpRequest");

            migrationBuilder.DropColumn(
                name: "Status",
                schema: "helprequests",
                table: "HelpRequest");

            migrationBuilder.DropColumn(
                name: "AssignedTeacherId",
                schema: "helprequests",
                table: "HelpRequest");

            migrationBuilder.RenameColumn(
                name: "StudentId",
                schema: "helprequests",
                table: "HelpRequest",
                newName: "UserId");

            migrationBuilder.AlterColumn<Guid>(
                name: "SessionId",
                schema: "helprequests",
                table: "HelpRequest",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<int>(
                name: "Generation",
                schema: "helprequests",
                table: "HelpRequest",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.Sql("""
                UPDATE helprequests."HelpRequest" SET "Generation" = NULL, "SessionId" = NULL
                    WHERE "SessionId" = '00000000-0000-0000-0000-000000000000'::uuid;
                UPDATE helprequests."HelpRequest" SET "UserId" = NULL WHERE "UserId" LIKE 'legacy:%';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_HelpRequest_SessionId_Generation_UserId",
                schema: "helprequests",
                table: "HelpRequest",
                columns: new[] { "SessionId", "Generation", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_HelpRequest_UserId",
                schema: "helprequests",
                table: "HelpRequest",
                column: "UserId");
        }
    }
}
