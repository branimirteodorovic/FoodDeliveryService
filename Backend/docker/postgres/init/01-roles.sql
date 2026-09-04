-- Feature 3.7 Milestone C — least-privilege database accounts.
--
-- Runs once, from the Postgres image's /docker-entrypoint-initdb.d, on an EMPTY data directory.
-- An existing volume has already been initialised and will NOT re-run this file: to pick up a
-- change, remove Backend/.containers/db (compose) or delete the StatefulSet's PVC (KinD).
--
-- What it establishes, and why:
--
--   * Every service owned all eight databases before this, because every service connected as the
--     superuser `postgres`. A SQL-injection or deserialisation bug in any one host was a
--     full-platform compromise, and Hard Rule #5 ("never query another service's tables") was
--     enforced by convention alone. It is enforced by the server now.
--
--   * Two roles per service. `fds_{service}_owner` owns the database and holds DDL rights; it is
--     used by exactly one code path, the startup EF Core migration (ConnectionStrings:DatabaseMigrations).
--     `fds_{service}_app` can CONNECT to its own database and run SELECT/INSERT/UPDATE/DELETE there
--     and nothing else — no CREATE, no other database. That is the credential every
--     request-serving connection pool holds (ConnectionStrings:Database).
--
--   * `REVOKE CONNECT ... FROM PUBLIC` is the line that actually does the isolating. Without it
--     every role can open every database, and the per-schema grants below only decide what it can
--     do once inside.
--
-- The passwords here are local-stack credentials in the same category as `postgres`/`postgres`:
-- valid only against a throwaway compose or KinD Postgres, and committed for the same reason the
-- rest of appsettings.Development.json is. They MUST differ per role — two roles sharing a password
-- would mean a leaked app credential also opens the owner account, which is the escalation this
-- whole file exists to prevent. docs/security.md §4.
--
-- Idempotent throughout: the integration suite mounts this same file into a Testcontainers Postgres
-- whose entrypoint has already created one of the databases.

\set ON_ERROR_STOP on

-- ---------------------------------------------------------------------------------------------
-- 1. The sixteen roles.
-- ---------------------------------------------------------------------------------------------
DO $$
DECLARE
    service text;
    role_name text;
BEGIN
    FOREACH service IN ARRAY ARRAY[
        'identity', 'users', 'orders', 'restaurants', 'notifications', 'delivery', 'realtime', 'support'
    ]
    LOOP
        FOREACH role_name IN ARRAY ARRAY['owner', 'app']
        LOOP
            IF NOT EXISTS (
                SELECT 1 FROM pg_roles WHERE rolname = format('fds_%s_%s', service, role_name)
            ) THEN
                EXECUTE format(
                    'CREATE ROLE %I LOGIN PASSWORD %L',
                    format('fds_%s_%s', service, role_name),
                    format('fds_%s_%s_dev', service, role_name));
            END IF;
        END LOOP;
    END LOOP;
END
$$;

-- ---------------------------------------------------------------------------------------------
-- 2. The eight databases, each owned by its service's owner role.
--
-- These used to be created by EF Core's Migrate() on first boot, as a side effect of connecting to
-- a database that did not exist. That cannot survive least privilege — CREATE DATABASE is a
-- cluster-level right no service account should hold — so the databases are created here instead,
-- already owned by the right role, and Migrate() now only ever evolves a schema.
-- ---------------------------------------------------------------------------------------------
SELECT format('CREATE DATABASE %I OWNER %I', 'fooddeliveryservice_' || s, 'fds_' || s || '_owner')
FROM unnest(ARRAY[
    'identity', 'users', 'orders', 'restaurants', 'notifications', 'delivery', 'realtime', 'support'
]) AS s
WHERE NOT EXISTS (SELECT 1 FROM pg_database d WHERE d.datname = 'fooddeliveryservice_' || s)
\gexec

-- Unconditional, because a database the container entrypoint created from POSTGRES_DB (which is
-- how the Testcontainers fixtures arrive here) exists already and is owned by `postgres`.
SELECT format('ALTER DATABASE %I OWNER TO %I', 'fooddeliveryservice_' || s, 'fds_' || s || '_owner')
FROM unnest(ARRAY[
    'identity', 'users', 'orders', 'restaurants', 'notifications', 'delivery', 'realtime', 'support'
]) AS s
\gexec

-- ---------------------------------------------------------------------------------------------
-- 3. Cluster-level CONNECT: nobody by default, the owning service's two roles by name.
-- ---------------------------------------------------------------------------------------------
SELECT format('REVOKE CONNECT ON DATABASE %I FROM PUBLIC', 'fooddeliveryservice_' || s)
FROM unnest(ARRAY[
    'identity', 'users', 'orders', 'restaurants', 'notifications', 'delivery', 'realtime', 'support'
]) AS s
\gexec

SELECT format('GRANT CONNECT ON DATABASE %I TO %I', 'fooddeliveryservice_' || s, 'fds_' || s || '_app')
FROM unnest(ARRAY[
    'identity', 'users', 'orders', 'restaurants', 'notifications', 'delivery', 'realtime', 'support'
]) AS s
\gexec

-- ---------------------------------------------------------------------------------------------
-- 4. In-database privileges, one block per database.
--
-- `\connect` is a psql client command: it cannot be looped or driven by \gexec, so this section is
-- eight copies of the same six statements. ALTER DEFAULT PRIVILEGES is the important one — the
-- tables do not exist yet when this file runs, so the app role is granted rights over whatever the
-- owner creates *later*, which is every table any future migration adds.
-- ---------------------------------------------------------------------------------------------

\connect fooddeliveryservice_identity

REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO fds_identity_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO fds_identity_app;
GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO fds_identity_app;
ALTER DEFAULT PRIVILEGES FOR ROLE fds_identity_owner IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO fds_identity_app;
ALTER DEFAULT PRIVILEGES FOR ROLE fds_identity_owner IN SCHEMA public
    GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO fds_identity_app;

\connect fooddeliveryservice_users

REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO fds_users_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO fds_users_app;
GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO fds_users_app;
ALTER DEFAULT PRIVILEGES FOR ROLE fds_users_owner IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO fds_users_app;
ALTER DEFAULT PRIVILEGES FOR ROLE fds_users_owner IN SCHEMA public
    GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO fds_users_app;

\connect fooddeliveryservice_orders

REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO fds_orders_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO fds_orders_app;
GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO fds_orders_app;
ALTER DEFAULT PRIVILEGES FOR ROLE fds_orders_owner IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO fds_orders_app;
ALTER DEFAULT PRIVILEGES FOR ROLE fds_orders_owner IN SCHEMA public
    GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO fds_orders_app;

\connect fooddeliveryservice_restaurants

REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO fds_restaurants_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO fds_restaurants_app;
GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO fds_restaurants_app;
ALTER DEFAULT PRIVILEGES FOR ROLE fds_restaurants_owner IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO fds_restaurants_app;
ALTER DEFAULT PRIVILEGES FOR ROLE fds_restaurants_owner IN SCHEMA public
    GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO fds_restaurants_app;

\connect fooddeliveryservice_notifications

REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO fds_notifications_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO fds_notifications_app;
GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO fds_notifications_app;
ALTER DEFAULT PRIVILEGES FOR ROLE fds_notifications_owner IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO fds_notifications_app;
ALTER DEFAULT PRIVILEGES FOR ROLE fds_notifications_owner IN SCHEMA public
    GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO fds_notifications_app;

\connect fooddeliveryservice_delivery

REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO fds_delivery_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO fds_delivery_app;
GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO fds_delivery_app;
ALTER DEFAULT PRIVILEGES FOR ROLE fds_delivery_owner IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO fds_delivery_app;
ALTER DEFAULT PRIVILEGES FOR ROLE fds_delivery_owner IN SCHEMA public
    GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO fds_delivery_app;

\connect fooddeliveryservice_realtime

REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO fds_realtime_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO fds_realtime_app;
GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO fds_realtime_app;
ALTER DEFAULT PRIVILEGES FOR ROLE fds_realtime_owner IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO fds_realtime_app;
ALTER DEFAULT PRIVILEGES FOR ROLE fds_realtime_owner IN SCHEMA public
    GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO fds_realtime_app;

\connect fooddeliveryservice_support

REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO fds_support_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO fds_support_app;
GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO fds_support_app;
ALTER DEFAULT PRIVILEGES FOR ROLE fds_support_owner IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO fds_support_app;
ALTER DEFAULT PRIVILEGES FOR ROLE fds_support_owner IN SCHEMA public
    GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO fds_support_app;
