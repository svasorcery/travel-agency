START TRANSACTION;
DROP TABLE support.action_receipts;

DROP TABLE support.messages;

DROP TABLE support.cases;

DELETE FROM support.__ef_migrations_history
WHERE migration_id = '20261012190847_AddSupportCaseWorkflow';

COMMIT;
