DO $$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'eirservice') THEN
        CREATE ROLE eirservice LOGIN PASSWORD 'EirService-dev-2026!';
    END IF;
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'mimirservice') THEN
        CREATE ROLE mimirservice LOGIN PASSWORD 'MimirService-dev-2026!';
    END IF;
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'gnaservice') THEN
        CREATE ROLE gnaservice LOGIN PASSWORD 'GnaService-dev-2026!';
    END IF;
END
$$;

SELECT 'CREATE DATABASE bifrost'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'bifrost')\gexec

\connect bifrost

GRANT CONNECT ON DATABASE bifrost TO eirservice, mimirservice, gnaservice;
GRANT USAGE ON SCHEMA public TO eirservice, mimirservice, gnaservice;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO eirservice, mimirservice, gnaservice;
GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO eirservice, mimirservice, gnaservice;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO eirservice, mimirservice, gnaservice;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO eirservice, mimirservice, gnaservice;
