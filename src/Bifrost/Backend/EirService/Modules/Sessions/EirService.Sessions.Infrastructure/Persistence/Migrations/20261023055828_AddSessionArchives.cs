using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EirService.Sessions.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionArchives : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CurrentGenerationStartedAtUtc",
                schema: "sessions",
                table: "sessions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.Sql("""
                UPDATE sessions.sessions
                SET "CurrentGenerationStartedAtUtc" = COALESCE("LastClearedAtUtc", "CreatedAtUtc");
                """);

            migrationBuilder.CreateTable(
                name: "archives",
                schema: "sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Generation = table.Column<int>(type: "integer", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ArchivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_archives", x => x.Id);
                    table.ForeignKey(
                        name: "FK_archives_sessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "sessions",
                        principalTable: "sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_archives_SessionId_Generation",
                schema: "sessions",
                table: "archives",
                columns: new[] { "SessionId", "Generation" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "archives",
                schema: "sessions");

            migrationBuilder.DropColumn(
                name: "CurrentGenerationStartedAtUtc",
                schema: "sessions",
                table: "sessions");
        }
    }
}
