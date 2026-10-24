using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EirService.HelpRequests.Infrastructure.Persistence.Migrations;

[DbContext(typeof(EirRequestDbContext))]
[Migration("20261023090000_ArchiveQueuedConversations")]
public sealed class ArchiveQueuedConversations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ArchivedConversation",
            schema: "helprequests",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                Generation = table.Column<int>(type: "integer", nullable: false),
                StudentId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Data = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_ArchivedConversation", snapshot => snapshot.Id));
        migrationBuilder.CreateIndex(
            name: "IX_ArchivedConversation_SessionId_Generation_StudentId",
            schema: "helprequests",
            table: "ArchivedConversation",
            columns: ["SessionId", "Generation", "StudentId"]);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "ArchivedConversation", schema: "helprequests");
}
