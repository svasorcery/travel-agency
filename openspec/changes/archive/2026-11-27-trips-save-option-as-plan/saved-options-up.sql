START TRANSACTION;
ALTER TABLE trips.trip_plans ADD protected_option text;

CREATE TABLE trips.saved_option_receipts (
    trip_id uuid NOT NULL,
    owner_issuer character varying(512) NOT NULL,
    owner_subject uuid NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_saved_option_receipts PRIMARY KEY (trip_id)
);

CREATE INDEX ix_saved_option_receipts_expires_at ON trips.saved_option_receipts (expires_at);

INSERT INTO trips.__ef_migrations_history (migration_id, product_version)
VALUES ('20261127164744_AddSavedOptions', '10.0.8');

COMMIT;
