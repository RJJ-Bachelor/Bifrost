using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimirService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHelpRequestSessionAndGeneration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Generation",
                table: "requests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "SessionId",
                table: "requests",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_requests_SessionId_Generation",
                table: "requests",
                columns: new[] { "SessionId", "Generation" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_requests_SessionId_Generation",
                table: "requests");

            migrationBuilder.DropColumn(
                name: "Generation",
                table: "requests");

            migrationBuilder.DropColumn(
                name: "SessionId",
                table: "requests");
        }
    }
}
