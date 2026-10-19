using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnaService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Change",
                table: "notifications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Generation",
                table: "notifications",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "HelpRequestId",
                table: "notifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SessionId",
                table: "notifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "notifications",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Change",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "Generation",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "HelpRequestId",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "SessionId",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "notifications");
        }
    }
}
