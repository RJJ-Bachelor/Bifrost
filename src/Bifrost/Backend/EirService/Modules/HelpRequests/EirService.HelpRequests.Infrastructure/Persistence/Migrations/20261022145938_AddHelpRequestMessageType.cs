using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EirService.HelpRequests.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHelpRequestMessageType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MessageType",
                schema: "helprequests",
                table: "HelpRequestMessage",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            // A shared answer immediately follows its system question. Keep that history even
            // when rejection or a follow-up has since cleared the request's FAQ reference.
            migrationBuilder.Sql("""
                WITH history AS (
                    SELECT "Id", LAG("AuthorRole") OVER (
                        PARTITION BY "HelpRequestId" ORDER BY "SequenceNumber") AS previous_role
                    FROM helprequests."HelpRequestMessage"
                )
                UPDATE helprequests."HelpRequestMessage" AS message
                SET "MessageType" = CASE
                    WHEN message."AuthorRole" = 'Student' AND message."SequenceNumber" = 1 THEN 'StudentQuestion'
                    WHEN message."AuthorRole" = 'Student' THEN 'FollowUpQuestion'
                    WHEN message."AuthorRole" = 'System' THEN 'FaqQuestion'
                    WHEN request."StudentId" IS NULL OR history.previous_role = 'System' THEN 'FaqAnswer'
                    ELSE 'TeacherAnswer'
                END
                FROM helprequests."HelpRequest" AS request, history
                WHERE message."HelpRequestId" = request."Id" AND message."Id" = history."Id";
                """);

            migrationBuilder.AlterColumn<string>(
                name: "MessageType",
                schema: "helprequests",
                table: "HelpRequestMessage",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MessageType",
                schema: "helprequests",
                table: "HelpRequestMessage");
        }
    }
}
