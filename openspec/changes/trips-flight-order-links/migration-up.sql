START TRANSACTION;
CREATE TABLE trips.trip_flight_links (
    trip_id uuid NOT NULL,
    owner_issuer character varying(512) NOT NULL,
    owner_subject uuid NOT NULL,
    links_revision bigint NOT NULL,
    protected_payload character varying(8192) NOT NULL,
    CONSTRAINT pk_trip_flight_links PRIMARY KEY (trip_id),
    CONSTRAINT ck_trip_flight_links_revision_positive CHECK (links_revision > 0),
    CONSTRAINT fk_trip_flight_links_trip_plans_trip_id FOREIGN KEY (trip_id) REFERENCES trips.trip_plans (trip_id) ON DELETE CASCADE
);

INSERT INTO trips.__ef_migrations_history (migration_id, product_version)
VALUES ('20261117192732_AddTripFlightLinks', '10.0.8');

COMMIT;
