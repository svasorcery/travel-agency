DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'trips') THEN
        CREATE SCHEMA trips;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS trips.__ef_migrations_history (
    migration_id character varying(150) NOT NULL,
    product_version character varying(32) NOT NULL,
    CONSTRAINT pk___ef_migrations_history PRIMARY KEY (migration_id)
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM trips.__ef_migrations_history WHERE "migration_id" = '20261020143618_AddTripsPlanWorkspace') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'trips') THEN
            CREATE SCHEMA trips;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM trips.__ef_migrations_history WHERE "migration_id" = '20261020143618_AddTripsPlanWorkspace') THEN
    CREATE TABLE trips.trip_plans (
        trip_id uuid NOT NULL,
        owner_issuer character varying(512) NOT NULL,
        owner_subject uuid NOT NULL,
        revision bigint NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        protected_payload text NOT NULL,
        CONSTRAINT pk_trip_plans PRIMARY KEY (trip_id),
        CONSTRAINT ck_trip_plans_revision_positive CHECK (revision > 0)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM trips.__ef_migrations_history WHERE "migration_id" = '20261020143618_AddTripsPlanWorkspace') THEN
    CREATE INDEX ix_trip_plans_owner_issuer_owner_subject_updated_at_trip_id ON trips.trip_plans (owner_issuer, owner_subject, updated_at DESC, trip_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM trips.__ef_migrations_history WHERE "migration_id" = '20261020143618_AddTripsPlanWorkspace') THEN
    INSERT INTO trips.__ef_migrations_history (migration_id, product_version)
    VALUES ('20261020143618_AddTripsPlanWorkspace', '10.0.8');
    END IF;
END $EF$;
COMMIT;
