using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xaquiamuni.Aplicacao.Catalogo;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

[Trait("Category", "SqlServerIntegration")]
public sealed partial class ReservarProdutoAcabadoSqlServerIntegrationTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 18, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Hoje = new(2026, 11, 18);
    private static readonly Guid Responsavel = Guid.NewGuid();

    [SqlServerIntegrationFact]
    public async Task ReservaUmLoteSemBaixaERepetePeloPedidoEVariante()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        Assert.NotNull(database.Services.GetRequiredService<IReservarProdutoAcabado>());
        await using var context = database.CreateVerificationContext();
        var variante = await CriarVarianteAsync(context);
        var lote = await EntrarAsync(context, variante.Id, 3);
        var casoDeUso = CasoDeUso(context);
        var input = new ReservarProdutoAcabadoInput(Guid.NewGuid(), variante.Id, 2);

        var primeiro = await casoDeUso.ExecuteAsync(input);
        var repetido = await casoDeUso.ExecuteAsync(input);
        var conflitante = await casoDeUso.ExecuteAsync(input with { Quantidade = 1 });

        Assert.True(primeiro.Sucesso);
        var reserva = Assert.Single(primeiro.Reservas);
        Assert.Equal(lote.Lote!.Id, reserva.LoteId);
        Assert.Equal(input.PedidoId, reserva.PedidoId);
        Assert.Equal(variante.Id, reserva.ProdutoVarianteId);
        Assert.Equal(2, reserva.Quantidade);
        Assert.Equal(TimeSpan.Zero, reserva.CriadaEmUtc.Offset);
        Assert.Equal(reserva.CriadaEmUtc.AddHours(24), reserva.ExpiraEmUtc);
        Assert.Equal(EstadoReservaProdutoAcabado.Ativa, reserva.Estado);
        Assert.True(repetido.Repetida);
        Assert.Equal(reserva.Id, Assert.Single(repetido.Reservas).Id);
        Assert.Equal(ReservarProdutoAcabadoStatus.ConflitoIdempotencia, conflitante.Status);
        Assert.Equal(reserva.Id, (await context.ReservasProdutoAcabado.SingleAsync()).Id);
        Assert.Equal(3, (await context.SaldosProdutoAcabado.SingleAsync()).QuantidadeFisica);
        Assert.Equal(1, await context.MovimentosProdutoAcabado.CountAsync());
    }

    [SqlServerIntegrationFact]
    public async Task SelecionaDoisLotesEmFefoComDesempateEReverteQuandoInsuficiente()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var variante = await CriarVarianteAsync(context);
        await EntrarAsync(context, variante.Id, 4, validade: Hoje.AddDays(3));
        var primeiraData = new[]
        {
            (await EntrarAsync(context, variante.Id, 2, validade: Hoje)).Lote!,
            (await EntrarAsync(context, variante.Id, 2, validade: Hoje)).Lote!
        }.OrderBy(lote => lote.Codigo, StringComparer.Ordinal).ToArray();

        var casoDeUso = CasoDeUso(context);
        var input = new ReservarProdutoAcabadoInput(Guid.NewGuid(), variante.Id, 9);
        var insuficiente = await casoDeUso.ExecuteAsync(input);
        Assert.Equal(ReservarProdutoAcabadoStatus.SaldoInsuficiente, insuficiente.Status);
        Assert.Empty(insuficiente.Reservas);
        Assert.Empty(await context.ReservasProdutoAcabado.ToListAsync());

        var resultado = await casoDeUso.ExecuteAsync(input with { Quantidade = 3 });
        Assert.True(resultado.Sucesso);
        Assert.Equal(primeiraData.Select(lote => lote.Id), resultado.Reservas.Select(reserva => reserva.LoteId));
        Assert.Equal([2, 1], resultado.Reservas.Select(reserva => reserva.Quantidade));
        Assert.Equal(2, await context.ReservasProdutoAcabado.CountAsync());
        Assert.Equal(8, (await context.SaldosProdutoAcabado.ToListAsync()).Sum(saldo => saldo.QuantidadeFisica));
    }

    [SqlServerIntegrationFact]
    public async Task DuasReservasConcorrentesTemUmVencedorEReplayConcorrenteMantemAIdentidade()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var variante = await CriarVarianteAsync(context);
        await EntrarAsync(context, variante.Id, 1);
        await using var primeiraConexao = database.CreateVerificationContext();
        await using var segundaConexao = database.CreateVerificationContext();
        var primeiroPedido = new ReservarProdutoAcabadoInput(Guid.NewGuid(), variante.Id, 1);
        var segundoPedido = primeiroPedido with { PedidoId = Guid.NewGuid() };

        var (primeiro, segundo) = await DisputarAsync(
            () => CasoDeUso(primeiraConexao).ExecuteAsync(primeiroPedido),
            () => CasoDeUso(segundaConexao).ExecuteAsync(segundoPedido));

        var vencedor = Assert.Single(new[] { primeiro, segundo }, resultado => resultado.Sucesso);
        Assert.Single(new[] { primeiro, segundo }, resultado => resultado.Status == ReservarProdutoAcabadoStatus.SaldoInsuficiente);
        var reserva = Assert.Single(vencedor.Reservas);
        var pedidoVencedor = primeiroPedido with { PedidoId = reserva.PedidoId };
        var (replayA, replayB) = await DisputarAsync(
            () => CasoDeUso(primeiraConexao).ExecuteAsync(pedidoVencedor),
            () => CasoDeUso(segundaConexao).ExecuteAsync(pedidoVencedor));

        Assert.All(new[] { replayA, replayB }, resultado =>
        {
            Assert.True(resultado.Repetida);
            Assert.Equal(reserva.Id, Assert.Single(resultado.Reservas).Id);
        });
        Assert.Equal(1, await context.ReservasProdutoAcabado.CountAsync());
        Assert.Equal(1, (await context.SaldosProdutoAcabado.SingleAsync()).QuantidadeFisica);
    }

    [SqlServerIntegrationFact]
    public async Task ReservaESaidaPerdaOuDescarteConcorremPelaMesmaUnidade()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var variante = await CriarVarianteAsync(context);
        await using var primeiraConexao = database.CreateVerificationContext();
        await using var segundaConexao = database.CreateVerificationContext();
        foreach (var tipo in new[]
        {
            TipoMovimentoProdutoAcabado.SaidaManual,
            TipoMovimentoProdutoAcabado.Perda,
            TipoMovimentoProdutoAcabado.Descarte
        })
        {
            var lote = (await EntrarAsync(context, variante.Id, 1)).Lote!;
            var input = new ReservarProdutoAcabadoInput(Guid.NewGuid(), variante.Id, 1);
            var (reserva, operacao) = await DisputarAsync(
                () => CasoDeUso(primeiraConexao).ExecuteAsync(input),
                () => Repositorio(segundaConexao).RegistrarOperacaoAsync(Operacao(variante.Id, lote.Id, tipo, 1)));

            Assert.NotEqual(reserva.Sucesso, operacao.Sucesso);
            if (reserva.Sucesso)
            {
                Assert.Equal(OperacaoProdutoAcabadoStatus.SaldoInsuficiente, operacao.Status);
            }
            else
            {
                Assert.Equal(ReservarProdutoAcabadoStatus.SaldoInsuficiente, reserva.Status);
            }

            Assert.Equal(reserva.Sucesso ? 1 : 0, await context.ReservasProdutoAcabado.CountAsync(r => r.LoteId == lote.Id));
            var saldo = await context.SaldosProdutoAcabado.AsNoTracking().SingleAsync(s => s.LoteId == lote.Id);
            Assert.Equal(reserva.Sucesso ? 1 : 0, saldo.QuantidadeFisica);
        }
    }

    [SqlServerIntegrationFact]
    public async Task DescontaReservasConfirmadasEAtivasValidasSemDescontarVencidas()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var variante = await CriarVarianteAsync(context);
        var loteConfirmado = (await EntrarAsync(context, variante.Id, 1, validade: Hoje)).Lote!;
        var loteAtivo = (await EntrarAsync(context, variante.Id, 1, validade: Hoje)).Lote!;
        var loteExpirado = (await EntrarAsync(context, variante.Id, 1)).Lote!;
        var agora = await context.Database.SqlQueryRaw<DateTimeOffset>(
            "SELECT TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00') AS [Value]").SingleAsync();
        var confirmada = ReservaProdutoAcabado.Criar(Guid.NewGuid(), variante.Id, loteConfirmado.Id, 1, agora.AddDays(-3));
        confirmada.ConfirmarCompromisso(confirmada.CriadaEmUtc.AddHours(1));
        var ativa = ReservaProdutoAcabado.Criar(Guid.NewGuid(), variante.Id, loteAtivo.Id, 1, agora.AddHours(-1));
        var expirada = ReservaProdutoAcabado.Criar(Guid.NewGuid(), variante.Id, loteExpirado.Id, 1, agora.AddHours(-25));
        context.ReservasProdutoAcabado.AddRange(confirmada, ativa, expirada);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var input = new ReservarProdutoAcabadoInput(Guid.NewGuid(), variante.Id, 1);
        var resultado = await CasoDeUso(context).ExecuteAsync(input);
        Assert.True(resultado.Sucesso);
        Assert.Equal(loteExpirado.Id, Assert.Single(resultado.Reservas).LoteId);
        var expiradaPersistida = await context.ReservasProdutoAcabado.SingleAsync(r => r.Id == expirada.Id);
        Assert.Equal(EstadoReservaProdutoAcabado.Ativa, expiradaPersistida.Estado);
        Assert.Equal(3, (await context.SaldosProdutoAcabado.ToListAsync()).Sum(saldo => saldo.QuantidadeFisica));
    }

    [SqlServerIntegrationFact]
    public async Task ExcluiLoteVencidoQuarentenaEVarianteInativa()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var variante = await CriarVarianteAsync(context);
        await EntrarAsync(context, variante.Id, 1, validade: Hoje.AddDays(-1));
        await EntrarAsync(context, variante.Id, 1, condicao: CondicaoEstoqueProdutoAcabado.Quarentena);
        await EntrarAsync(context, variante.Id, 1, condicao: CondicaoEstoqueProdutoAcabado.Danificada);
        var input = new ReservarProdutoAcabadoInput(Guid.NewGuid(), variante.Id, 1);
        Assert.Equal(ReservarProdutoAcabadoStatus.SaldoInsuficiente, (await CasoDeUso(context).ExecuteAsync(input)).Status);
        await EntrarAsync(context, variante.Id, 1);
        Assert.Equal(ProdutoStatus.Success, (await DesativarAsync(context, variante)).Status);

        Assert.Equal(ReservarProdutoAcabadoStatus.VarianteInativa, (await CasoDeUso(context).ExecuteAsync(input)).Status);
        Assert.Empty(await context.ReservasProdutoAcabado.ToListAsync());
    }

    [SqlServerIntegrationFact]
    public async Task BloqueiaTransferenciaContagemEstornoEDesativacaoQueInvalidariamReserva()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var variante = await CriarVarianteAsync(context);
        var abertura = await EntrarAsync(context, variante.Id, 2);
        var loteId = abertura.Lote!.Id;
        var casoDeUso = CasoDeUso(context);
        Assert.True((await casoDeUso.ExecuteAsync(new(Guid.NewGuid(), variante.Id, 2))).Sucesso);
        var versao = await context.SaldosProdutoAcabado.AsNoTracking()
            .Select(saldo => EF.Property<byte[]>(saldo, "Versao")).SingleAsync();
        var repositorio = Repositorio(context);

        foreach (var tipo in new[]
        {
            TipoMovimentoProdutoAcabado.SaidaManual,
            TipoMovimentoProdutoAcabado.Perda,
            TipoMovimentoProdutoAcabado.Descarte
        })
        {
            var operacao = await repositorio.RegistrarOperacaoAsync(Operacao(variante.Id, loteId, tipo, 1));
            Assert.Equal(OperacaoProdutoAcabadoStatus.SaldoInsuficiente, operacao.Status);
        }

        var transferencia = await repositorio.RegistrarOperacaoAsync(Operacao(
            variante.Id, loteId, TipoMovimentoProdutoAcabado.TransferenciaCondicao, 1)
            with { CondicaoDestino = CondicaoEstoqueProdutoAcabado.Quarentena });
        var contagem = await repositorio.ContarAsync(new(
            variante.Id, loteId, Responsavel, 1, 0, 1, "Contagem", "contagem", versao));
        var estorno = await repositorio.EstornarAsync(new(abertura.Movimento!.Id, Responsavel, "Estorno", "estorno"));
        var catalogo = await DesativarAsync(context, variante);

        Assert.Equal(OperacaoProdutoAcabadoStatus.SaldoInsuficiente, transferencia.Status);
        Assert.Equal(OperacaoProdutoAcabadoStatus.SaldoInsuficiente, contagem.Status);
        Assert.Equal(OperacaoProdutoAcabadoStatus.SaldoInsuficiente, estorno.Status);
        Assert.Equal(ProdutoStatus.Conflict, catalogo.Status);
        Assert.True(await context.ProdutoVariantes.Where(v => v.Id == variante.Id).Select(v => v.Ativa).SingleAsync());
        Assert.Equal(2, (await context.SaldosProdutoAcabado.SingleAsync()).QuantidadeVendavel);
        Assert.Equal(1, await context.MovimentosProdutoAcabado.CountAsync());
    }

    [SqlServerIntegrationFact]
    public async Task ReservaEDesativacaoConcorrentesNaoPodemVencerJuntas()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateVerificationContext();
        var variante = await CriarVarianteAsync(context);
        await EntrarAsync(context, variante.Id, 1);
        await using var primeiraConexao = database.CreateVerificationContext();
        await using var segundaConexao = database.CreateVerificationContext();

        var (reserva, catalogo) = await DisputarAsync(
            () => CasoDeUso(primeiraConexao).ExecuteAsync(new(Guid.NewGuid(), variante.Id, 1)),
            () => DesativarAsync(segundaConexao, variante));

        Assert.Equal(reserva.Sucesso ? ProdutoStatus.Conflict : ProdutoStatus.Success, catalogo.Status);
        Assert.Equal(reserva.Sucesso ? ReservarProdutoAcabadoStatus.Sucesso : ReservarProdutoAcabadoStatus.VarianteInativa,
            reserva.Status);
        Assert.Equal(reserva.Sucesso, await context.ProdutoVariantes
            .Where(v => v.Id == variante.Id).Select(v => v.Ativa).SingleAsync());
        Assert.Equal(reserva.Sucesso ? 1 : 0, await context.ReservasProdutoAcabado.CountAsync());
    }

    private static async Task<(TA, TB)> DisputarAsync<TA, TB>(Func<Task<TA>> primeira, Func<Task<TB>> segunda)
    {
        var inicio = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tarefaA = AguardarInicioAsync(primeira);
        var tarefaB = AguardarInicioAsync(segunda);
        inicio.SetResult();
        await Task.WhenAll(tarefaA, tarefaB);
        return (await tarefaA, await tarefaB);

        async Task<T> AguardarInicioAsync<T>(Func<Task<T>> operacao)
        {
            await inicio.Task;
            return await operacao();
        }
    }

    private static Task<ProdutoResult> DesativarAsync(XaquiamuniDbContext context, ProdutoVariante variante) =>
        new EfProdutoRepository(context).ExecutarMutacaoAsync(async (sessao, token) =>
        {
            var produto = await sessao.BuscarProdutoAsync(variante.ProdutoId, token);
            produto!.DesativarVariante(variante.Id);
            return ProdutoResult.Success(produto);
        });

    private static async Task<ProdutoVariante> CriarVarianteAsync(
        XaquiamuniDbContext context, string categoriaNome = "Sabonetes")
    {
        var categoria = Categoria.Criar(categoriaNome, null, 0);
        var produto = Produto.Criar("Sabonete", null, categoria,
            [ProdutoVariante.Criar("Padrão", "UN", 10m, padrao: true), ProdutoVariante.Criar("Lavanda", "UN", 10m)]);
        context.Produtos.Add(produto);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return produto.Variantes.Single(variante => !variante.Padrao);
    }

    private static async Task<RegistrarEntradaProdutoAcabadoResult> EntrarAsync(
        XaquiamuniDbContext context,
        Guid varianteId,
        int quantidade,
        DateOnly? validade = null,
        CondicaoEstoqueProdutoAcabado condicao = CondicaoEstoqueProdutoAcabado.Vendavel)
    {
        var resultado = await Repositorio(context).RegistrarEntradaAsync(new(
            varianteId, Responsavel, TipoMovimentoProdutoAcabado.EntradaManual, quantidade, condicao,
            "Entrada de teste", Guid.NewGuid().ToString("N"),
            DataFabricacaoNovoLote: Hoje.AddDays(-3), DataValidadeNovoLote: validade ?? Hoje.AddDays(10)));
        Assert.True(resultado.Sucesso, resultado.Erro);
        return resultado;
    }

    private static RegistrarOperacaoProdutoAcabadoInput Operacao(
        Guid varianteId, Guid loteId, TipoMovimentoProdutoAcabado tipo, int quantidade) =>
        new(varianteId, loteId, Responsavel, tipo, quantidade, CondicaoEstoqueProdutoAcabado.Vendavel,
            null, "Operação de teste", Guid.NewGuid().ToString("N"));

    private static ReservarProdutoAcabado CasoDeUso(XaquiamuniDbContext context) =>
        new(Repositorio(context));

    private static EfEstoqueProdutoAcabadoRepository Repositorio(XaquiamuniDbContext context) =>
        new(context, new RelogioFixo(), new CalendarioFixo());

    private sealed class RelogioFixo : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Agora;
    }

    private sealed class CalendarioFixo : ICalendarioOperacionalEstoque
    {
        public DateOnly ObterDataAtual() => Hoje;
        public DateOnly ObterDataEm(DateTimeOffset instanteUtc) => Hoje;
    }
}
