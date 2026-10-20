using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EirService.Participants.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddParticipantRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_session_participants",
                schema: "participants",
                table: "session_participants");

            migrationBuilder.DropIndex(
                name: "IX_session_participants_StudentSubjectId",
                schema: "participants",
                table: "session_participants");

            migrationBuilder.RenameColumn(
                name: "StudentSubjectId",
                schema: "participants",
                table: "session_participants",
                newName: "SubjectId");

            migrationBuilder.AddColumn<string>(
                name: "Role",
                schema: "participants",
                table: "session_participants",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Student");

            // Backfill existing memberships as students; future inserts must specify their role.
            migrationBuilder.Sql("""
                ALTER TABLE participants.session_participants ALTER COLUMN "Role" DROP DEFAULT;
                """);

            migrationBuilder.AddPrimaryKey(
                name: "PK_session_participants",
                schema: "participants",
                table: "session_participants",
                columns: new[] { "SessionId", "SubjectId", "Role" });

            migrationBuilder.CreateIndex(
                name: "IX_session_participants_SubjectId_Role",
                schema: "participants",
                table: "session_participants",
                columns: new[] { "SubjectId", "Role" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The old model only supports students. Never turn teachers into student memberships.
            migrationBuilder.Sql("""
                DELETE FROM participants.session_participants WHERE "Role" <> 'Student';
                """);

            migrationBuilder.DropPrimaryKey(
                name: "PK_session_participants",
                schema: "participants",
                table: "session_participants");

            migrationBuilder.DropIndex(
                name: "IX_session_participants_SubjectId_Role",
                schema: "participants",
                table: "session_participants");

            migrationBuilder.DropColumn(
                name: "Role",
                schema: "participants",
                table: "session_participants");

            migrationBuilder.RenameColumn(
                name: "SubjectId",
                schema: "participants",
                table: "session_participants",
                newName: "StudentSubjectId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_session_participants",
                schema: "participants",
                table: "session_participants",
                columns: new[] { "SessionId", "StudentSubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_session_participants_StudentSubjectId",
                schema: "participants",
                table: "session_participants",
                column: "StudentSubjectId");
        }
    }
}
