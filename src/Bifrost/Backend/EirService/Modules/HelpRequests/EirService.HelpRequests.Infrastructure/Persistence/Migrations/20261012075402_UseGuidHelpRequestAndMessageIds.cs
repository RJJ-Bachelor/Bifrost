using Microsoft.EntityFrameworkCore.Migrations;
using Shared.Infrastructure.Persistence.Migrations;

#nullable disable

namespace EirService.HelpRequests.Infrastructure.Persistence.Migrations;

public partial class UseGuidHelpRequestAndMessageIds : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(GuidIdMigration.ToGuid("helprequests", "HelpRequest", "Id"));
        migrationBuilder.Sql(GuidIdMigration.ToGuid("helprequests", "outbox", "MessageId"));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(GuidIdMigration.ToString("helprequests", "outbox", "MessageId", 200));
        migrationBuilder.Sql(GuidIdMigration.ToString("helprequests", "HelpRequest", "Id", 100));
    }
}
