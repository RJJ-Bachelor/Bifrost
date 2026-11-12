using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EirService.Participants.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentIdentityTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "identity_transfers",
                schema: "participants",
                columns: table => new
                {
                    Code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    StudentId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_transfers", x => x.Code);
                });

            migrationBuilder.CreateIndex(
                name: "IX_identity_transfers_ExpiresAtUtc",
                schema: "participants",
                table: "identity_transfers",
                column: "ExpiresAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "identity_transfers",
                schema: "participants");
        }
    }
}
