using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EirService.HelpRequests.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFaqQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HelpRequest_OneOpenPerStudent",
                schema: "helprequests",
                table: "HelpRequest");

            migrationBuilder.AlterColumn<string>(
                name: "StudentId",
                schema: "helprequests",
                table: "HelpRequest",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.CreateIndex(
                name: "IX_HelpRequest_OneOpenPerStudent",
                schema: "helprequests",
                table: "HelpRequest",
                columns: new[] { "SessionId", "Generation", "StudentId" },
                unique: true,
                filter: "\"Status\" IN ('Pending', 'Active', 'AwaitingFaq', 'AwaitingConfirmation')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HelpRequest_OneOpenPerStudent",
                schema: "helprequests",
                table: "HelpRequest");

            migrationBuilder.AlterColumn<string>(
                name: "StudentId",
                schema: "helprequests",
                table: "HelpRequest",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_HelpRequest_OneOpenPerStudent",
                schema: "helprequests",
                table: "HelpRequest",
                columns: new[] { "SessionId", "Generation", "StudentId" },
                unique: true,
                filter: "\"Status\" IN ('Pending', 'Active', 'AwaitingConfirmation')");
        }
    }
}
