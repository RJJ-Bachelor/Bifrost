using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EirService.HelpRequests.Infrastructure.Persistence.Migrations;

public partial class InitialHelpRequests : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Adopt the existing EnsureCreated table without losing legacy requests.
        // A fresh database receives the same final schema.
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS public.requests (
                "Id" character varying(100) NOT NULL,
                "Message" character varying(4000) NOT NULL,
                "UserId" character varying(100) NULL,
                "SessionId" uuid NULL,
                "Generation" integer NULL,
                CONSTRAINT "PK_requests" PRIMARY KEY ("Id")
            );
            ALTER TABLE public.requests ADD COLUMN IF NOT EXISTS "UserId" character varying(100) NULL;
            ALTER TABLE public.requests ADD COLUMN IF NOT EXISTS "SessionId" uuid NULL;
            ALTER TABLE public.requests ADD COLUMN IF NOT EXISTS "Generation" integer NULL;
            CREATE INDEX IF NOT EXISTS "IX_requests_UserId" ON public.requests ("UserId");
            CREATE INDEX IF NOT EXISTS "IX_requests_SessionId_Generation_UserId"
                ON public.requests ("SessionId", "Generation", "UserId");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The adopted table and ownership predate this feature; keep their data.
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS public."IX_requests_SessionId_Generation_UserId";
            ALTER TABLE public.requests DROP COLUMN IF EXISTS "Generation";
            ALTER TABLE public.requests DROP COLUMN IF EXISTS "SessionId";
            """);
    }
}
