using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EirService.Participants.Infrastructure.Persistence.Migrations;

public partial class AddParticipantDisplayNames : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(
            name: "DisplayName",
            schema: "participants",
            table: "session_participants",
            type: "text",
            nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(
            name: "DisplayName",
            schema: "participants",
            table: "session_participants");
}
