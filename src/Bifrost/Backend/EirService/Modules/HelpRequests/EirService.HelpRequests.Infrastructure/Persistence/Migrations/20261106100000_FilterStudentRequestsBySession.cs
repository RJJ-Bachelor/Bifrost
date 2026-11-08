using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EirService.HelpRequests.Infrastructure.Persistence.Migrations;

[DbContext(typeof(EirRequestDbContext))]
[Migration("20261106100000_FilterStudentRequestsBySession")]
public sealed class FilterStudentRequestsBySession : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_HelpRequest_StudentId_SessionId_QueuedAt_Id",
            schema: "helprequests",
            table: "HelpRequest",
            columns: new[] { "StudentId", "SessionId", "QueuedAt", "Id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_HelpRequest_StudentId_SessionId_QueuedAt_Id",
            schema: "helprequests",
            table: "HelpRequest");
    }
}
