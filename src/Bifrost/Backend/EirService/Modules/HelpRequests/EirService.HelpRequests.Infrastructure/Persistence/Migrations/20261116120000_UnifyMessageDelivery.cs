using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shared.Infrastructure.Persistence.Messaging;

namespace EirService.HelpRequests.Infrastructure.Persistence.Migrations;

[DbContext(typeof(EirRequestDbContext))]
[Migration("20261116120000_UnifyMessageDelivery")]
public sealed class UnifyMessageDelivery : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        MessageDeliveryMigrationV1.Up(migrationBuilder, "helprequests", true);
    protected override void Down(MigrationBuilder migrationBuilder) =>
        MessageDeliveryMigrationV1.Down(migrationBuilder, "helprequests", true);
}
