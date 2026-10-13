using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EirService.HelpRequests.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HelpRequestNumbersAndTeacherReturn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HelpRequest_SessionId_Generation_QueuedAt_Id",
                schema: "helprequests",
                table: "HelpRequest");

            migrationBuilder.AddColumn<int>(
                name: "RequestNumber",
                schema: "helprequests",
                table: "HelpRequest",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReturnedToQueueAt",
                schema: "helprequests",
                table: "HelpRequest",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HelpRequestNumberCounter",
                schema: "helprequests",
                columns: table => new
                {
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Generation = table.Column<int>(type: "integer", nullable: false),
                    LastNumber = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HelpRequestNumberCounter", x => new { x.SessionId, x.Generation });
                });

            migrationBuilder.Sql("""
                WITH numbered AS (
                    SELECT "Id", ROW_NUMBER() OVER (
                        PARTITION BY "SessionId", "Generation" ORDER BY "CreatedAt", "Id")::integer AS number
                    FROM helprequests."HelpRequest"
                )
                UPDATE helprequests."HelpRequest" AS request
                SET "RequestNumber" = numbered.number
                FROM numbered WHERE request."Id" = numbered."Id";

                INSERT INTO helprequests."HelpRequestNumberCounter" ("SessionId", "Generation", "LastNumber")
                SELECT "SessionId", "Generation", MAX("RequestNumber")
                FROM helprequests."HelpRequest" GROUP BY "SessionId", "Generation";

                CREATE FUNCTION helprequests.assign_request_number() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    INSERT INTO helprequests."HelpRequestNumberCounter" AS counter ("SessionId", "Generation", "LastNumber")
                    VALUES (NEW."SessionId", NEW."Generation", 1)
                    ON CONFLICT ("SessionId", "Generation") DO UPDATE
                    SET "LastNumber" = counter."LastNumber" + 1
                    RETURNING "LastNumber" INTO NEW."RequestNumber";
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER assign_request_number BEFORE INSERT ON helprequests."HelpRequest"
                FOR EACH ROW EXECUTE FUNCTION helprequests.assign_request_number();
                """);

            migrationBuilder.CreateIndex(
                name: "IX_HelpRequest_SessionId_Generation_RequestNumber",
                schema: "helprequests",
                table: "HelpRequest",
                columns: new[] { "SessionId", "Generation", "RequestNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HelpRequest_SessionId_Generation_ReturnedToQueueAt_QueuedAt~",
                schema: "helprequests",
                table: "HelpRequest",
                columns: new[] { "SessionId", "Generation", "ReturnedToQueueAt", "QueuedAt", "Id" },
                descending: new[] { false, false, true, false, false },
                filter: "\"Status\" = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER assign_request_number ON helprequests."HelpRequest";
                DROP FUNCTION helprequests.assign_request_number();
                """);

            migrationBuilder.DropTable(
                name: "HelpRequestNumberCounter",
                schema: "helprequests");

            migrationBuilder.DropIndex(
                name: "IX_HelpRequest_SessionId_Generation_RequestNumber",
                schema: "helprequests",
                table: "HelpRequest");

            migrationBuilder.DropIndex(
                name: "IX_HelpRequest_SessionId_Generation_ReturnedToQueueAt_QueuedAt~",
                schema: "helprequests",
                table: "HelpRequest");

            migrationBuilder.DropColumn(
                name: "RequestNumber",
                schema: "helprequests",
                table: "HelpRequest");

            migrationBuilder.DropColumn(
                name: "ReturnedToQueueAt",
                schema: "helprequests",
                table: "HelpRequest");

            migrationBuilder.CreateIndex(
                name: "IX_HelpRequest_SessionId_Generation_QueuedAt_Id",
                schema: "helprequests",
                table: "HelpRequest",
                columns: new[] { "SessionId", "Generation", "QueuedAt", "Id" },
                filter: "\"Status\" = 'Pending'");
        }
    }
}
