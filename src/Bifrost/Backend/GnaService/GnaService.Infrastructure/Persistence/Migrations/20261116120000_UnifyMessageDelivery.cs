using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shared.Infrastructure.Persistence.Messaging;

namespace GnaService.Infrastructure.Persistence.Migrations;

[DbContext(typeof(GnaNotificationDbContext))]
[Migration("20261116120000_UnifyMessageDelivery")]
public sealed class UnifyMessageDelivery : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        MessageDeliveryMigrationV1.Up(migrationBuilder, null, false);
    protected override void Down(MigrationBuilder migrationBuilder) =>
        MessageDeliveryMigrationV1.Down(migrationBuilder, null, false);
}
