START TRANSACTION;
DROP TABLE trips.trip_flight_links;

DELETE FROM trips.__ef_migrations_history
WHERE migration_id = '20261117192732_AddTripFlightLinks';

COMMIT;
