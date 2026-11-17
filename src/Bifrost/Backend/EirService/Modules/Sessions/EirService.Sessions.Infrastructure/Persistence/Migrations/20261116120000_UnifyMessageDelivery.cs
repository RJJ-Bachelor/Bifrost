using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shared.Infrastructure.Persistence.Messaging;

namespace EirService.Sessions.Infrastructure.Persistence.Migrations;

[DbContext(typeof(EirSessionDbContext))]
[Migration("20261116120000_UnifyMessageDelivery")]
public sealed class UnifyMessageDelivery : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        MessageDeliveryMigrationV1.Up(migrationBuilder, "sessions", false);
    protected override void Down(MigrationBuilder migrationBuilder) =>
        MessageDeliveryMigrationV1.Down(migrationBuilder, "sessions", false);
}
