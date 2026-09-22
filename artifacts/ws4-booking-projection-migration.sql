DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'flights') THEN
        CREATE SCHEMA flights;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS flights.__ef_migrations_history (
    migration_id character varying(150) NOT NULL,
    product_version character varying(32) NOT NULL,
    CONSTRAINT pk___ef_migrations_history PRIMARY KEY (migration_id)
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM flights.__ef_migrations_history WHERE "migration_id" = '20260513153403_FlightsM1Init') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'flights') THEN
            CREATE SCHEMA flights;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM flights.__ef_migrations_history WHERE "migration_id" = '20260513153403_FlightsM1Init') THEN
    CREATE TABLE flights.deeplink_offers_cache (
        id uuid NOT NULL,
        criteria_hash text NOT NULL,
        offers_json jsonb NOT NULL,
        fetched_at timestamp with time zone NOT NULL,
        expires_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_deeplink_offers_cache PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM flights.__ef_migrations_history WHERE "migration_id" = '20260513153403_FlightsM1Init') THEN
    CREATE TABLE flights.idempotency_keys (
        key text NOT NULL,
        user_id uuid NOT NULL,
        route text NOT NULL,
        body_hash text NOT NULL,
        response_hash text,
        response_status integer NOT NULL,
        response_body text,
        created_at timestamp with time zone NOT NULL,
        expires_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_idempotency_keys PRIMARY KEY (key)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM flights.__ef_migrations_history WHERE "migration_id" = '20260513153403_FlightsM1Init') THEN
    CREATE TABLE flights.order_read_model (
        id uuid NOT NULL,
        aggregate_id uuid NOT NULL,
        user_id uuid,
        provider_order_id text,
        status text NOT NULL,
        total_amount numeric NOT NULL,
        currency text NOT NULL,
        itinerary_json jsonb NOT NULL,
        passenger_info_json jsonb NOT NULL,
        ticket_numbers text[] NOT NULL,
        booked_at timestamp with time zone NOT NULL,
        ticketed_at timestamp with time zone,
        cancelled_at timestamp with time zone,
        refunded_at timestamp with time zone,
        CONSTRAINT pk_order_read_model PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM flights.__ef_migrations_history WHERE "migration_id" = '20260513153403_FlightsM1Init') THEN
    CREATE TABLE flights.webhook_inbox (
        id uuid NOT NULL,
        source text NOT NULL,
        event_id text NOT NULL,
        event_type text NOT NULL,
        raw_payload jsonb NOT NULL,
        signature text NOT NULL,
        received_at timestamp with time zone NOT NULL,
        processed_at timestamp with time zone,
        CONSTRAINT pk_webhook_inbox PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM flights.__ef_migrations_history WHERE "migration_id" = '20260513153403_FlightsM1Init') THEN
    CREATE INDEX ix_deeplink_offers_cache_criteria_hash_expires_at ON flights.deeplink_offers_cache (criteria_hash, expires_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM flights.__ef_migrations_history WHERE "migration_id" = '20260513153403_FlightsM1Init') THEN
    CREATE INDEX ix_idempotency_keys_expires_at ON flights.idempotency_keys (expires_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM flights.__ef_migrations_history WHERE "migration_id" = '20260513153403_FlightsM1Init') THEN
    CREATE UNIQUE INDEX ix_order_read_model_aggregate_id ON flights.order_read_model (aggregate_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM flights.__ef_migrations_history WHERE "migration_id" = '20260513153403_FlightsM1Init') THEN
    CREATE INDEX ix_order_read_model_user_id_booked_at ON flights.order_read_model (user_id, booked_at DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM flights.__ef_migrations_history WHERE "migration_id" = '20260513153403_FlightsM1Init') THEN
    CREATE UNIQUE INDEX ix_webhook_inbox_source_event_id ON flights.webhook_inbox (source, event_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM flights.__ef_migrations_history WHERE "migration_id" = '20260513153403_FlightsM1Init') THEN
    INSERT INTO flights.__ef_migrations_history (migration_id, product_version)
    VALUES ('20260513153403_FlightsM1Init', '10.0.8');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM flights.__ef_migrations_history WHERE "migration_id" = '20260922132058_AddOrderReadModelProjectedStreamVersion') THEN
    ALTER TABLE flights.order_read_model ADD projected_stream_version bigint NOT NULL DEFAULT -1;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM flights.__ef_migrations_history WHERE "migration_id" = '20260922132058_AddOrderReadModelProjectedStreamVersion') THEN
    INSERT INTO flights.__ef_migrations_history (migration_id, product_version)
    VALUES ('20260922132058_AddOrderReadModelProjectedStreamVersion', '10.0.8');
    END IF;
END $EF$;
COMMIT;
