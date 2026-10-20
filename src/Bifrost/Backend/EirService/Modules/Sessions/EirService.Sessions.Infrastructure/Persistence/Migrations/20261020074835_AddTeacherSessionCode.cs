using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EirService.Sessions.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTeacherSessionCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TeacherCode",
                schema: "sessions",
                table: "sessions",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            // Preserve student codes. Generate independent codes with the same 32-character
            // alphabet and entropy as SessionCode.Generate, without requiring pgcrypto.
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    session_id uuid;
                    teacher_code text;
                BEGIN
                    FOR session_id IN SELECT "Id" FROM sessions.sessions ORDER BY "Id" LOOP
                        LOOP
                            SELECT string_agg(substr('ABCDEFGHJKLMNPQRSTUVWXYZ23456789',
                                (get_byte(uuid_send(gen_random_uuid()), 0) % 32) + 1, 1), '' ORDER BY n)
                            INTO teacher_code
                            FROM generate_series(1, 8) AS characters(n);
                            EXIT WHEN NOT EXISTS (
                                SELECT 1 FROM sessions.sessions
                                WHERE "Code" = teacher_code OR "TeacherCode" = teacher_code);
                        END LOOP;
                        UPDATE sessions.sessions SET "TeacherCode" = teacher_code WHERE "Id" = session_id;
                    END LOOP;
                END $$;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "TeacherCode",
                schema: "sessions",
                table: "sessions",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(8)",
                oldMaxLength: 8,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_sessions_TeacherCode",
                schema: "sessions",
                table: "sessions",
                column: "TeacherCode",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_sessions_DistinctCodes",
                schema: "sessions",
                table: "sessions",
                sql: "\"Code\" <> \"TeacherCode\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_sessions_TeacherCode",
                schema: "sessions",
                table: "sessions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_sessions_DistinctCodes",
                schema: "sessions",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "TeacherCode",
                schema: "sessions",
                table: "sessions");
        }
    }
}
