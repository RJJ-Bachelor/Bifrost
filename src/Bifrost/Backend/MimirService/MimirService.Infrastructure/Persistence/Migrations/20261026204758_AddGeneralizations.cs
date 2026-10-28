using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimirService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGeneralizations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GeneralizationId",
                table: "requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "generalizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Generation = table.Column<int>(type: "integer", nullable: false),
                    Question = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_generalizations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_requests_GeneralizationId",
                table: "requests",
                column: "GeneralizationId");

            migrationBuilder.CreateIndex(
                name: "IX_generalizations_SessionId_Generation",
                table: "generalizations",
                columns: new[] { "SessionId", "Generation" });

            migrationBuilder.AddForeignKey(
                name: "FK_requests_generalizations_GeneralizationId",
                table: "requests",
                column: "GeneralizationId",
                principalTable: "generalizations",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_requests_generalizations_GeneralizationId",
                table: "requests");

            migrationBuilder.DropTable(
                name: "generalizations");

            migrationBuilder.DropIndex(
                name: "IX_requests_GeneralizationId",
                table: "requests");

            migrationBuilder.DropColumn(
                name: "GeneralizationId",
                table: "requests");
        }
    }
}
