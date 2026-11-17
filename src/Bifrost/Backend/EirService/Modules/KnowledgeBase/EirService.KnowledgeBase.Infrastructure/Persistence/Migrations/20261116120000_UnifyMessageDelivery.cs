using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shared.Infrastructure.Persistence.Messaging;

namespace EirService.KnowledgeBase.Infrastructure.Persistence.Migrations;

[DbContext(typeof(EirKnowledgeBaseDbContext))]
[Migration("20261116120000_UnifyMessageDelivery")]
public sealed class UnifyMessageDelivery : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        MessageDeliveryMigrationV1.Up(migrationBuilder, "knowledgebase", true);
    protected override void Down(MigrationBuilder migrationBuilder) =>
        MessageDeliveryMigrationV1.Down(migrationBuilder, "knowledgebase", true);
}
