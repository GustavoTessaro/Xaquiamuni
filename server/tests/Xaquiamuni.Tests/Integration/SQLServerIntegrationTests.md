# SQL Server integration tests

These tests are skipped by default. They require a local SQL Server Express instance named `.\SQLEXPRESS` and Windows permission to create and drop databases.

From the repository root, opt in explicitly:

```powershell
$env:XAQUIAMUNI_RUN_SQLSERVER_INTEGRATION = '1'
dotnet test server\Xaquiamuni.slnx --filter "Category=SqlServerIntegration"
Remove-Item Env:\XAQUIAMUNI_RUN_SQLSERVER_INTEGRATION
```

Each test checks the exact generated database name before creation, applies migrations only to that database, and drops only the exact database created by that test. The tests do not use `XaquiamuniDev`, other project databases, or cloud services. CPF keys are generated in memory.

The customer-registration integration tests also verify that confirmation requests are committed with the user, role, profile, and address; that enqueue and final-persistence failures roll the transaction back; and that SQL Server enforces the active Outbox deduplication index.

The Outbox persistence integration tests enable `READ_COMMITTED_SNAPSHOT` only on their own generated database and cover competing claims, expired-lease recovery, stale row versions, retries, and dead-letter transitions.
