namespace Shared.Infrastructure.Persistence.Migrations;

// Converts legacy IDs consistently across services, outboxes and existing foreign keys.
public static class GuidIdMigration
{
    public static string ToGuid(string schema, string table, string column) => """
        CREATE OR REPLACE FUNCTION pg_temp.bifrost_guid_id(value text) RETURNS uuid
        LANGUAGE sql IMMUTABLE STRICT AS $function$
            SELECT CASE
                WHEN value ~* '^([0-9a-f]{32}|[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})$'
                    THEN value::uuid
                ELSE md5('bifrost:id:' || value)::uuid
            END;
        $function$;
        """ + ConvertColumn(schema, table, column, "uuid", "pg_temp.bifrost_guid_id(%I::text)");

    public static string ToString(string schema, string table, string column, int previousLength) =>
        ConvertColumn(schema, table, column, $"character varying({previousLength})", "%I::text");

    private static string ConvertColumn(string schema, string table, string column, string dataType, string conversion)
    {
        var relation = $"\"{schema.Replace("\"", "\"\"")}\".\"{table.Replace("\"", "\"\"")}\"".Replace("'", "''");
        var columnName = column.Replace("'", "''");
        return $$"""
            DO $migration$
            DECLARE
                foreign_keys jsonb;
                foreign_key_data jsonb;
            BEGIN
                SELECT COALESCE(jsonb_agg(jsonb_build_object(
                    'table', foreign_key.conrelid::regclass::text,
                    'name', foreign_key.conname,
                    'definition', pg_get_constraintdef(foreign_key.oid),
                    'column', source_column.attname)), '[]'::jsonb)
                INTO foreign_keys
                FROM pg_constraint foreign_key
                JOIN LATERAL unnest(foreign_key.confkey, foreign_key.conkey) AS keys(target, source) ON true
                JOIN pg_attribute target_column ON target_column.attrelid = foreign_key.confrelid AND target_column.attnum = keys.target
                JOIN pg_attribute source_column ON source_column.attrelid = foreign_key.conrelid AND source_column.attnum = keys.source
                WHERE foreign_key.contype = 'f' AND foreign_key.confrelid = '{{relation}}'::regclass AND target_column.attname = '{{columnName}}';

                FOR foreign_key_data IN SELECT * FROM jsonb_array_elements(foreign_keys) LOOP
                    EXECUTE format('ALTER TABLE %s DROP CONSTRAINT %I', foreign_key_data->>'table', foreign_key_data->>'name');
                END LOOP;
                FOR foreign_key_data IN SELECT * FROM jsonb_array_elements(foreign_keys) LOOP
                    EXECUTE format('ALTER TABLE %s ALTER COLUMN %I TYPE {{dataType}} USING %s',
                        foreign_key_data->>'table', foreign_key_data->>'column', format('{{conversion}}', foreign_key_data->>'column'));
                END LOOP;
                EXECUTE format('ALTER TABLE %s ALTER COLUMN %I TYPE {{dataType}} USING %s',
                    '{{relation}}', '{{columnName}}', format('{{conversion}}', '{{columnName}}'));
                FOR foreign_key_data IN SELECT * FROM jsonb_array_elements(foreign_keys) LOOP
                    EXECUTE format('ALTER TABLE %s ADD CONSTRAINT %I %s', foreign_key_data->>'table', foreign_key_data->>'name', foreign_key_data->>'definition');
                END LOOP;
            END $migration$;
            """;
    }
}
