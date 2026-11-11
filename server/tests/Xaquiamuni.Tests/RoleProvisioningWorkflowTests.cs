using Xaquiamuni.Admin;

namespace Xaquiamuni.Tests;

public class RoleProvisioningWorkflowTests
{
    [Fact]
    public void ValidateConnectionString_AcceptsOnlyExplicitLocalDevelopmentTarget()
    {
        var builder = RoleProvisioningTargetGuard.ValidateConnectionString(
            @"Server=.\SQLEXPRESS;Database=XaquiamuniDev;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;");

        Assert.Equal(@".\SQLEXPRESS", builder.DataSource);
        Assert.Equal("XaquiamuniDev", builder.InitialCatalog);
        Assert.True(builder.IntegratedSecurity);
    }

    [Theory]
    [InlineData(@"Server=.\SQLEXPRESS;Database=master;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;")]
    [InlineData(@"Server=.\SQLEXPRESS;Database=NoteKeeper_GustavoTessaro_Dev;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;")]
    [InlineData(@"Server=tcp:example.database.windows.net,1433;Database=XaquiamuniDev;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;")]
    [InlineData(@"Server=.\SQLEXPRESS;Database=XaquiamuniDev;User ID=test;Password=test;Encrypt=True;TrustServerCertificate=True;")]
    [InlineData(@"Server=.\SQLEXPRESS;Database=XaquiamuniDev;Integrated Security=True;Encrypt=False;TrustServerCertificate=True;")]
    public void ValidateConnectionString_RejectsAnyOtherOrWeakTarget(string connectionString)
    {
        Assert.Throws<RoleProvisioningPreconditionException>(
            () => RoleProvisioningTargetGuard.ValidateConnectionString(connectionString));
    }

    [Theory]
    [InlineData("MSSQLSERVER", "WORKSTATION", "XaquiamuniDev", "WORKSTATION")]
    [InlineData("SQLEXPRESS", "REMOTE\\SQLEXPRESS", "XaquiamuniDev", "WORKSTATION")]
    [InlineData("SQLEXPRESS", "WORKSTATION\\SQLEXPRESS", "master", "WORKSTATION")]
    public void ValidateConnectedTarget_RejectsWrongInstanceServerOrDatabase(
        string instanceName,
        string serverName,
        string databaseName,
        string localMachineName)
    {
        Assert.Throws<RoleProvisioningPreconditionException>(() =>
            RoleProvisioningTargetGuard.ValidateConnectedTarget(
                instanceName,
                serverName,
                databaseName,
                localMachineName));
    }

    [Fact]
    public void ValidateConnectedTarget_AcceptsExpectedLocalInstanceAndDatabase()
    {
        RoleProvisioningTargetGuard.ValidateConnectedTarget(
            "SQLEXPRESS",
            "WORKSTATION\\SQLEXPRESS",
            "XaquiamuniDev",
            "WORKSTATION");
    }

    [Fact]
    public void ValidateAppliedMigrations_RequiresBothExpectedMigrations()
    {
        Assert.Throws<RoleProvisioningPreconditionException>(() =>
            RoleProvisioningTargetGuard.ValidateAppliedMigrations(
                [RoleProvisioningTargetGuard.InitialIdentityMigration]));
    }

    [Fact]
    public void ValidateAppliedMigrations_AcceptsBothExpectedMigrations()
    {
        RoleProvisioningTargetGuard.ValidateAppliedMigrations(
        [
            RoleProvisioningTargetGuard.InitialIdentityMigration,
            RoleProvisioningTargetGuard.ClienteProfileMigration
        ]);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutExactConfirmationDoesNotPreflightOrInitialize()
    {
        var preflight = new FakePreflight();
        var initializeCalls = 0;
        var workflow = new RoleProvisioningWorkflow(preflight, _ =>
        {
            initializeCalls++;
            return Task.CompletedTask;
        });

        var result = await workflow.ExecuteAsync("sim");

        Assert.Equal(RoleProvisioningCommandStatus.NotConfirmed, result);
        Assert.Equal(0, preflight.Calls);
        Assert.Equal(0, initializeCalls);
    }

    [Fact]
    public async Task ExecuteAsync_WhenPreflightFailsDoesNotInitialize()
    {
        var preflight = new FakePreflight
        {
            Exception = new RoleProvisioningPreconditionException("wrong target")
        };
        var initializeCalls = 0;
        var workflow = new RoleProvisioningWorkflow(preflight, _ =>
        {
            initializeCalls++;
            return Task.CompletedTask;
        });

        await Assert.ThrowsAsync<RoleProvisioningPreconditionException>(
            () => workflow.ExecuteAsync(RoleProvisioningWorkflow.ConfirmationText));

        Assert.Equal(1, preflight.Calls);
        Assert.Equal(0, initializeCalls);
    }

    [Fact]
    public async Task ExecuteAsync_InitializesOnlyAfterExactConfirmationAndSuccessfulPreflight()
    {
        var preflight = new FakePreflight();
        var initializeCalls = 0;
        var workflow = new RoleProvisioningWorkflow(preflight, _ =>
        {
            initializeCalls++;
            return Task.CompletedTask;
        });

        var result = await workflow.ExecuteAsync(RoleProvisioningWorkflow.ConfirmationText);

        Assert.Equal(RoleProvisioningCommandStatus.Completed, result);
        Assert.Equal(1, preflight.Calls);
        Assert.Equal(1, initializeCalls);
    }

    private sealed class FakePreflight : IRoleProvisioningPreflight
    {
        public int Calls { get; private set; }
        public Exception? Exception { get; init; }

        public Task ValidateAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Exception is null ? Task.CompletedTask : Task.FromException(Exception);
        }
    }
}
