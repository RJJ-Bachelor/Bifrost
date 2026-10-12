using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EirService.Participants.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialParticipants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "participants");

            migrationBuilder.CreateTable(
                name: "session_participants",
                schema: "participants",
                columns: table => new
                {
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentSubjectId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    JoinedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_session_participants", x => new { x.SessionId, x.StudentSubjectId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_session_participants_StudentSubjectId",
                schema: "participants",
                table: "session_participants",
                column: "StudentSubjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "session_participants",
                schema: "participants");
        }
    }
}
