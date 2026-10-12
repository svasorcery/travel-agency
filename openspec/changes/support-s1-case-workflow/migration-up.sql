DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'support') THEN
        CREATE SCHEMA support;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS support.__ef_migrations_history (
    migration_id character varying(150) NOT NULL,
    product_version character varying(32) NOT NULL,
    CONSTRAINT pk___ef_migrations_history PRIMARY KEY (migration_id)
);

START TRANSACTION;
DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'support') THEN
        CREATE SCHEMA support;
    END IF;
END $EF$;

CREATE TABLE support.cases (
    case_id uuid NOT NULL,
    owner_id uuid NOT NULL,
    first_message_id uuid NOT NULL,
    protected_title character varying(32768) NOT NULL,
    title_written_at_revision integer NOT NULL,
    state character varying(32) NOT NULL,
    revision integer NOT NULL,
    category character varying(32) NOT NULL,
    priority character varying(16) NOT NULL,
    triage_source character varying(16) NOT NULL,
    order_id uuid,
    order_stream_version bigint,
    order_verified_at timestamp with time zone,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    resolved_at timestamp with time zone,
    purge_at timestamp with time zone,
    CONSTRAINT pk_cases PRIMARY KEY (case_id),
    CONSTRAINT ck_cases_order_link CHECK ((order_id IS NULL AND order_stream_version IS NULL AND order_verified_at IS NULL) OR (order_id IS NOT NULL AND order_stream_version IS NOT NULL AND order_stream_version > 0 AND order_verified_at IS NOT NULL)),
    CONSTRAINT ck_cases_resolution CHECK ((state = 'Resolved' AND resolved_at IS NOT NULL AND purge_at IS NOT NULL) OR (state <> 'Resolved' AND resolved_at IS NULL AND purge_at IS NULL)),
    CONSTRAINT ck_cases_revision CHECK (revision > 0 AND title_written_at_revision > 0)
);

CREATE TABLE support.action_receipts (
    case_id uuid NOT NULL,
    action_id uuid NOT NULL,
    kind character varying(32) NOT NULL,
    actor_id uuid NOT NULL,
    actor_kind character varying(16) NOT NULL,
    expected_revision integer NOT NULL,
    applied_revision integer NOT NULL,
    applied_state character varying(32) NOT NULL,
    applied_at timestamp with time zone NOT NULL,
    message_id uuid,
    requested_order_id uuid,
    previous_category character varying(32),
    previous_priority character varying(16),
    requested_category character varying(32),
    requested_priority character varying(16),
    CONSTRAINT pk_action_receipts PRIMARY KEY (case_id, action_id),
    CONSTRAINT ck_receipts_revision CHECK (expected_revision >= 0 AND applied_revision > 0),
    CONSTRAINT fk_action_receipts_cases_case_id FOREIGN KEY (case_id) REFERENCES support.cases (case_id) ON DELETE CASCADE
);

CREATE TABLE support.messages (
    case_id uuid NOT NULL,
    message_id uuid NOT NULL,
    actor_id uuid NOT NULL,
    actor_kind character varying(16) NOT NULL,
    protected_body character varying(32768) NOT NULL,
    written_at_revision integer NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_messages PRIMARY KEY (case_id, message_id),
    CONSTRAINT ck_messages_revision CHECK (written_at_revision > 0),
    CONSTRAINT fk_messages_cases_case_id FOREIGN KEY (case_id) REFERENCES support.cases (case_id) ON DELETE CASCADE
);

CREATE INDEX ix_cases_created_at_case_id ON support.cases (created_at, case_id);

CREATE INDEX ix_cases_owner_id_created_at_case_id ON support.cases (owner_id, created_at, case_id);

CREATE INDEX ix_cases_state_purge_at_case_id ON support.cases (state, purge_at, case_id);

CREATE UNIQUE INDEX ix_messages_case_id_written_at_revision_message_id ON support.messages (case_id, written_at_revision, message_id);

INSERT INTO support.__ef_migrations_history (migration_id, product_version)
VALUES ('20261012190847_AddSupportCaseWorkflow', '10.0.8');

COMMIT;
