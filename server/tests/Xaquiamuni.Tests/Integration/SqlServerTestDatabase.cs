using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xaquiamuni.Aplicacao.Cadastro;
using Xaquiamuni.Infraestrutura;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Persistencia;
using Xaquiamuni.Infraestrutura.Seguranca;

namespace Xaquiamuni.Tests.Integration;

public sealed class SqlServerTestDatabase : IAsyncDisposable
{
    private const string ExpectedInstance = "SQLEXPRESS";
    private readonly string _databaseName;
    private readonly string _connectionString;
    private ServiceProvider? _serviceProvider;
    private AsyncServiceScope? _scope;
    private bool _databaseCreated;

    private SqlServerTestDatabase(string databaseName, string connectionString)
    {
        _databaseName = databaseName;
        _connectionString = connectionString;
    }

    public IServiceProvider Services => _scope?.ServiceProvider
        ?? throw new InvalidOperationException("The SQL Server test database is not initialized.");

    public static async Task<SqlServerTestDatabase> CreateAsync(bool failProfilePersistence = false)
    {
        var databaseName = $"Xaquiamuni_IntegrationTest_{Guid.NewGuid():N}";
        var masterConnectionString = new SqlConnectionStringBuilder
        {
            DataSource = @".\SQLEXPRESS",
            InitialCatalog = "master",
            IntegratedSecurity = true,
            Encrypt = true,
            TrustServerCertificate = true,
            ConnectTimeout = 5
        }.ConnectionString;

        await VerifyInstanceAndCreateDatabaseAsync(masterConnectionString, databaseName).ConfigureAwait(false);

        var databaseConnectionString = new SqlConnectionStringBuilder(masterConnectionString)
        {
            InitialCatalog = databaseName
        }.ConnectionString;
        var database = new SqlServerTestDatabase(databaseName, databaseConnectionString)
        {
            _databaseCreated = true
        };

        try
        {
            await database.InitializeAsync(failProfilePersistence).ConfigureAwait(false);
            return database;
        }
        catch
        {
            await database.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public XaquiamuniDbContext CreateVerificationContext()
    {
        var options = new DbContextOptionsBuilder<XaquiamuniDbContext>()
            .UseSqlServer(_connectionString)
            .Options;
        return new XaquiamuniDbContext(options);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_scope is not null)
            {
                await _scope.Value.DisposeAsync().ConfigureAwait(false);
                _scope = null;
            }

            if (_serviceProvider is not null)
            {
                await _serviceProvider.DisposeAsync().ConfigureAwait(false);
                _serviceProvider = null;
            }
        }
        finally
        {
            if (_databaseCreated)
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    SqlConnection.ClearPool(connection);
                }

                await DropCreatedDatabaseAsync(_databaseName).ConfigureAwait(false);
                _databaseCreated = false;
            }
        }
    }

    private async Task InitializeAsync(bool failProfilePersistence)
    {
        var encryptionKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var fingerprintKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SqlServer"] = _connectionString,
                ["CpfProtection:CurrentEncryptionKeyVersion"] = "enc-integration",
                ["CpfProtection:EncryptionKeys:enc-integration"] = Convert.ToBase64String(encryptionKey),
                ["CpfProtection:CurrentFingerprintKeyVersion"] = "fp-integration",
                ["CpfProtection:FingerprintKeys:fp-integration"] = Convert.ToBase64String(fingerprintKey)
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructureServices(configuration);
        if (failProfilePersistence)
        {
            services.RemoveAll<IClienteTransactionFactory>();
            services.AddScoped<IClienteTransactionFactory>(serviceProvider =>
                new FailOnFinalSaveTransactionFactory(
                    new EfClienteTransactionFactory(
                        serviceProvider.GetRequiredService<XaquiamuniDbContext>())));
        }

        _serviceProvider = services.BuildServiceProvider(validateScopes: true);
        _scope = _serviceProvider.CreateAsyncScope();
        var dbContext = Services.GetRequiredService<XaquiamuniDbContext>();
        await dbContext.Database.MigrateAsync().ConfigureAwait(false);

        var roleManager = Services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var roleResult = await roleManager.CreateAsync(new IdentityRole<Guid>(XaquiamuniRoles.Cliente))
            .ConfigureAwait(false);
        if (!roleResult.Succeeded)
        {
            throw new InvalidOperationException("Could not prepare the CLIENTE role in the isolated test database.");
        }
    }

    private static async Task VerifyInstanceAndCreateDatabaseAsync(
        string masterConnectionString,
        string databaseName)
    {
        await using var connection = new SqlConnection(masterConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await using (var instanceCommand = connection.CreateCommand())
        {
            instanceCommand.CommandText = """
                SELECT CONVERT(nvarchar(128), SERVERPROPERTY('InstanceName')),
                       CONVERT(nvarchar(128), SERVERPROPERTY('ServerName'));
                """;
            await using var reader = await instanceCommand.ExecuteReaderAsync().ConfigureAwait(false);
            if (!await reader.ReadAsync().ConfigureAwait(false)
                || !string.Equals(reader.GetString(0), ExpectedInstance, StringComparison.OrdinalIgnoreCase)
                || !reader.GetString(1).EndsWith(@"\SQLEXPRESS", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("SQL Server connection is not the local .\\SQLEXPRESS instance.");
            }
        }

        await using (var existenceCommand = connection.CreateCommand())
        {
            existenceCommand.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE name = @databaseName;";
            existenceCommand.Parameters.Add(new SqlParameter("@databaseName", SqlDbType.NVarChar, 128)
            {
                Value = databaseName
            });
            var exists = Convert.ToInt32(await existenceCommand.ExecuteScalarAsync().ConfigureAwait(false));
            if (exists != 0)
            {
                throw new InvalidOperationException("The generated SQL Server test database name already exists.");
            }
        }

        var quotedName = QuoteIdentifier(databaseName);
        await using var createCommand = connection.CreateCommand();
        createCommand.CommandText = $"CREATE DATABASE {quotedName};";
        await createCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task DropCreatedDatabaseAsync(string databaseName)
    {
        var masterConnectionString = new SqlConnectionStringBuilder
        {
            DataSource = @".\SQLEXPRESS",
            InitialCatalog = "master",
            IntegratedSecurity = true,
            Encrypt = true,
            TrustServerCertificate = true,
            ConnectTimeout = 5
        }.ConnectionString;

        await using var connection = new SqlConnection(masterConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        var quotedName = QuoteIdentifier(databaseName);
        command.CommandText = $"""
            IF DB_ID(N'{databaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE {quotedName} SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE {quotedName};
            END;
            """;
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static string QuoteIdentifier(string value) =>
        $"[{value.Replace("]", "]]", StringComparison.Ordinal)}]";

    private sealed class FailOnFinalSaveTransactionFactory(
        IClienteTransactionFactory inner) : IClienteTransactionFactory
    {
        public async Task<IClienteTransaction> BeginTransactionAsync(
            CancellationToken cancellationToken = default)
        {
            var transaction = await inner.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            return new FailOnFinalSaveTransaction(transaction);
        }
    }

    private sealed class FailOnFinalSaveTransaction(IClienteTransaction inner) : IClienteTransaction
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new ClientePersistenceException();

        public Task CommitAsync(CancellationToken cancellationToken = default) =>
            inner.CommitAsync(cancellationToken);

        public Task RollbackAsync(CancellationToken cancellationToken = default) =>
            inner.RollbackAsync(cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
