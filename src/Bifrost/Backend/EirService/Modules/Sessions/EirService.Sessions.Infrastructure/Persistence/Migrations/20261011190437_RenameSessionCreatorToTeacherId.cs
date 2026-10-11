using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EirService.Sessions.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameSessionCreatorToTeacherId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "CreatedBySubjectId",
                schema: "sessions",
                table: "sessions",
                newName: "CreatedByTeacherId");

            migrationBuilder.RenameIndex(
                name: "IX_sessions_CreatedBySubjectId",
                schema: "sessions",
                table: "sessions",
                newName: "IX_sessions_CreatedByTeacherId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "CreatedByTeacherId",
                schema: "sessions",
                table: "sessions",
                newName: "CreatedBySubjectId");

            migrationBuilder.RenameIndex(
                name: "IX_sessions_CreatedByTeacherId",
                schema: "sessions",
                table: "sessions",
                newName: "IX_sessions_CreatedBySubjectId");
        }
    }
}
