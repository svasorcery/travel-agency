START TRANSACTION;
LOCK TABLE trips.trip_plans, trips.saved_option_receipts IN ACCESS EXCLUSIVE MODE;
DO $guard$
BEGIN
    IF EXISTS (SELECT 1 FROM trips.trip_plans WHERE protected_option IS NOT NULL)
       OR EXISTS (SELECT 1 FROM trips.saved_option_receipts) THEN
        RAISE EXCEPTION 'Saved options or receipts remain; preserve the expanded schema';
    END IF;
END
$guard$;

DROP TABLE trips.saved_option_receipts;

ALTER TABLE trips.trip_plans DROP COLUMN protected_option;

DELETE FROM trips.__ef_migrations_history
WHERE migration_id = '20261127164744_AddSavedOptions';

COMMIT;
