using Microsoft.EntityFrameworkCore.Migrations;
using Shared.Infrastructure.Persistence.Migrations;

#nullable disable

namespace MimirService.Infrastructure.Persistence.Migrations;

public partial class UseGuidHelpRequestAndMessageIds : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(GuidIdMigration.ToGuid("public", "requests", "Id"));
        migrationBuilder.Sql(GuidIdMigration.ToGuid("public", "outbox", "MessageId"));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(GuidIdMigration.ToString("public", "outbox", "MessageId", 200));
        migrationBuilder.Sql(GuidIdMigration.ToString("public", "requests", "Id", 100));
    }
}
