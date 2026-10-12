using Microsoft.EntityFrameworkCore.Migrations;
using Shared.Infrastructure.Persistence.Migrations;

#nullable disable

namespace GnaService.Infrastructure.Persistence.Migrations;

public partial class UseGuidNotificationIds : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Adopt the table created by the previous EnsureCreated startup, or create it on a new database.
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS public.notifications (
                "Id" character varying(100) NOT NULL,
                "UserId" character varying(100) NOT NULL,
                "Messages" character varying(4000) NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_notifications" PRIMARY KEY ("Id")
            );
            """);
        migrationBuilder.Sql(GuidIdMigration.ToGuid("public", "notifications", "Id"));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(GuidIdMigration.ToString("public", "notifications", "Id", 100));
        // Keep the adopted notifications table and its existing data.
    }
}
