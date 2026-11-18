using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Xaquiamuni.Tests.Integration;

internal sealed class AlvoSqlE2E(string banco) : DbConnectionInterceptor
{
    internal const string Prefixo = "Xaquiamuni_IntegrationTest_";

    internal void ValidarConfiguracao(string connectionString)
    {
        var alvo = new SqlConnectionStringBuilder(connectionString);
        if (!banco.StartsWith(Prefixo, StringComparison.Ordinal)
            || !Guid.TryParseExact(banco[Prefixo.Length..], "N", out var id) || id == Guid.Empty
            || alvo.DataSource != @".\SQLEXPRESS" || alvo.InitialCatalog != banco
            || !alvo.IntegratedSecurity || !string.IsNullOrEmpty(alvo.AttachDBFilename) || alvo.UserInstance
            || !string.IsNullOrEmpty(alvo.FailoverPartner) || !string.IsNullOrEmpty(alvo.UserID)
            || !string.IsNullOrEmpty(alvo.Password))
        {
            throw new InvalidOperationException("Alvo SQL recusado. Utilize a base descartável desta execução.");
        }
    }

    public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData,
        InterceptionResult result)
    {
        ValidarConfiguracao(connection.ConnectionString);
        return result;
    }

    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
        ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(ConnectionOpening(connection, eventData, result));

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var comando = CriarVerificacao(connection);
        using var dados = comando.ExecuteReader();
        VerificarServidorEDatabase(dados, dados.Read());
    }

    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default) =>
        ValidarConexaoAbertaAsync(connection, cancellationToken);

    internal async Task ValidarConexaoAbertaAsync(DbConnection connection, CancellationToken cancellationToken = default)
    {
        await using var comando = CriarVerificacao(connection);
        await using var dados = await comando.ExecuteReaderAsync(cancellationToken);
        VerificarServidorEDatabase(dados, await dados.ReadAsync(cancellationToken));
    }

    private static DbCommand CriarVerificacao(DbConnection connection)
    {
        var comando = connection.CreateCommand();
        comando.CommandText = """
            SELECT CONVERT(nvarchar(128), SERVERPROPERTY('MachineName')),
                   CONVERT(nvarchar(128), SERVERPROPERTY('InstanceName')), DB_NAME();
            """;
        return comando;
    }

    private void VerificarServidorEDatabase(DbDataReader dados, bool encontrado)
    {
        if (!encontrado || dados.IsDBNull(0) || dados.IsDBNull(1)
            || !string.Equals(dados.GetString(0), Environment.MachineName, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(dados.GetString(1), "SQLEXPRESS", StringComparison.OrdinalIgnoreCase)
            || dados.GetString(2) != banco)
        {
            throw new InvalidOperationException("A identidade SQL efetiva diverge da base descartável desta execução.");
        }
    }
}
