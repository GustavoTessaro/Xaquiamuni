using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

[Trait("Category", "SqlServerIntegration")]
public sealed class ReservaProducaoSqlServerIntegrationTests
{
    private static readonly Guid Autor = Guid.NewGuid();
    private static readonly DateTimeOffset Agora = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Hoje = DateOnly.FromDateTime(Agora.UtcDateTime);
    private static readonly UnidadeFisicaEstoque Grama = UnidadeFisicaEstoque.Grama;

    [SqlServerIntegrationFact]
    public async Task AgregadoProtegeSaidaPerdaDescarteContagemEstornoEInativacaoAteLiberar()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var db = database.CreateVerificationContext();
        var material = Material("Base");
        var receita = await SemearAsync(db, (material, 7));
        var repo = Repositorio(db);
        var entrada = await EntradaAsync(repo, material, 10);
        var ordem = OrdemProducao.Criar(receita, 1, Autor, Agora);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.PrepararReservaProducaoAsync(ordem, default));
        Assert.True(await ReservarAsync(db, ordem));

        var detalhe = (await new EfEstoqueInsumoConsultaRepository(db).ConsultarInsumoAsync(material.Id, Hoje))!;
        var posicao = Assert.Single(detalhe.Posicoes);
        Assert.Equal((10m, 10m, 7m, 3m), (posicao.SaldoFisico, posicao.SaldoUtilizavel, posicao.SaldoReservado, posicao.SaldoDisponivel));
        foreach (var tipo in new[] { TipoMovimentoInsumo.SaidaManual, TipoMovimentoInsumo.Perda, TipoMovimentoInsumo.Descarte })
        {
            var operacao = await repo.RegistrarSaidaAsync(new(material.Id, Autor, tipo, 4, Grama, null, "Teste", Chave()));
            Assert.Equal(OperacaoEstoqueInsumoStatus.SaldoInsuficiente, operacao.Status);
            Assert.Contains("produções", operacao.Erro);
        }
        var contagem = await repo.AjustarContagemAsync(new(material.Id, Autor, 6, Grama, null, posicao.Versao, "Teste", Chave()));
        Assert.Equal(OperacaoEstoqueInsumoStatus.SaldoInsuficiente, contagem.Status);
        var estorno = await repo.EstornarMovimentoAsync(new(entrada.Id, Autor, "Teste", Chave()));
        Assert.Equal(OperacaoEstoqueInsumoStatus.SaldoInsuficiente, estorno.Status);
        var inativacao = await repo.AlterarStatusInsumoAsync(material.Id, false);
        Assert.Equal(AdministrarInsumoEstoqueStatus.Conflito, inativacao.Status);
        Assert.Equal(1, await db.MovimentosInsumo.CountAsync());
        Assert.Equal(OperacaoEstoqueInsumoStatus.Sucesso, (await repo.RegistrarSaidaAsync(
            new(material.Id, Autor, TipoMovimentoInsumo.SaidaManual, 3, Grama, null, "Teste", Chave()))).Status);

        await using (var transacao = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            Assert.True(await repo.PrepararLiberacaoProducaoAsync(ordem.Id, Autor, Agora, default));
            await db.SaveChangesAsync();
            await transacao.CommitAsync();
        }
        Assert.Equal(1, await db.AlocacoesMateriasPrimas.CountAsync());
        var depois = (await new EfEstoqueInsumoConsultaRepository(db).ConsultarInsumoAsync(material.Id, Hoje))!.Resumo;
        Assert.Equal((7m, 7m, 0m, 7m), (depois.SaldoFisico, depois.SaldoUtilizavel, depois.SaldoReservado, depois.SaldoDisponivel));
        Assert.Equal(OperacaoEstoqueInsumoStatus.Sucesso, (await repo.RegistrarSaidaAsync(
            new(material.Id, Autor, TipoMovimentoInsumo.SaidaManual, 7, Grama, null, "Teste", Chave()))).Status);
        Assert.Equal(AdministrarInsumoEstoqueStatus.Sucesso, (await repo.AlterarStatusInsumoAsync(material.Id, false)).Status);
    }

    [SqlServerIntegrationFact]
    public async Task FefoComHistoricoDeVencimentoEInsuficienciaEmOutroMaterial()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var db = database.CreateVerificationContext();
        var erva = Material("Erva", true);
        var embalagem = Material("Embalagem");
        var receita = await SemearAsync(db, (erva, 12), (embalagem, 1));
        var repo = Repositorio(db);
        await EntradaAsync(repo, erva, 30, "Vencido", Hoje.AddDays(-1), entradaEm: Hoje.AddDays(-2));
        await EntradaAsync(repo, erva, 20, "Bloqueado", Hoje.AddDays(1), true);
        var primeiro = await EntradaAsync(repo, erva, 3, "Hoje", Hoje);
        var datadoA = await EntradaAsync(repo, erva, 2, "A", Hoje.AddDays(2));
        var datadoB = await EntradaAsync(repo, erva, 3, "B", Hoje.AddDays(2));
        var semData = await EntradaAsync(repo, erva, 4, "Mais antigo", entradaEm: Hoje.AddDays(-1));
        var semDataNovo = await EntradaAsync(repo, erva, 5, "Mais novo");
        var ordem = OrdemProducao.Criar(receita, 1, Autor, Agora);

        Assert.False(await ReservarAsync(db, ordem));
        Assert.False(await db.OrdensProducao.AnyAsync());
        Assert.False(await db.AlocacoesMateriasPrimas.AnyAsync());
        await EntradaAsync(repo, embalagem, 2);
        Assert.True(await ReservarAsync(db, ordem));
        Assert.False(await ReservarAsync(db, OrdemProducao.Criar(receita, 1, Autor, Agora)));
        var parcelas = await db.AlocacoesMateriasPrimas.Where(a => a.Necessidade.InsumoId == erva.Id)
            .Select(a => new { LoteId = a.Saldo.LoteId!.Value, a.Reservada })
            .ToDictionaryAsync(a => a.LoteId, a => a.Reservada);
        Assert.Equal(4, parcelas.Count);
        Assert.Equal(3m, parcelas[primeiro.LoteId!.Value]);
        Assert.Equal(2m, parcelas[datadoA.LoteId!.Value]);
        Assert.Equal(3m, parcelas[datadoB.LoteId!.Value]);
        Assert.Equal(4m, parcelas[semData.LoteId!.Value]);
        Assert.DoesNotContain(semDataNovo.LoteId!.Value, parcelas.Keys);

        var consulta = new EfEstoqueInsumoConsultaRepository(db);
        var detalhe = (await consulta.ConsultarInsumoAsync(erva.Id, Hoje.AddDays(1)))!;
        var vencida = Assert.Single(detalhe.Posicoes, p => p.LoteId == primeiro.LoteId);
        Assert.Equal((3m, 0m, 3m, 0m), (vencida.SaldoFisico, vencida.SaldoUtilizavel, vencida.SaldoReservado, vencida.SaldoDisponivel));
        Assert.Equal((67m, 14m, 12m, 5m), (detalhe.Resumo.SaldoFisico, detalhe.Resumo.SaldoUtilizavel,
            detalhe.Resumo.SaldoReservado, detalhe.Resumo.SaldoDisponivel));
        var lista = await consulta.ListarInsumosAsync(new(Nome: "Erva"), Hoje.AddDays(1));
        Assert.Equal(detalhe.Resumo, Assert.Single(lista.Itens));
    }

    [SqlServerIntegrationFact]
    public async Task ReservaESaidaConcorrentesNaoComprometemOMesmoSaldo()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var seed = database.CreateVerificationContext();
        var material = Material("Base");
        var receita = await SemearAsync(seed, (material, 7m));
        await EntradaAsync(Repositorio(seed), material, 10m);
        await using var dbReserva = database.CreateVerificationContext();
        await using var dbSaida = database.CreateVerificationContext();
        var reservar = ReservarAsync(dbReserva, OrdemProducao.Criar(receita, 1, Autor, Agora));
        var sair = Repositorio(dbSaida).RegistrarSaidaAsync(new(material.Id, Autor, TipoMovimentoInsumo.SaidaManual,
            7, Grama, null, "Teste", Chave()));
        await Task.WhenAll(reservar, sair);
        Assert.NotEqual(await reservar, (await sair).Status == OperacaoEstoqueInsumoStatus.Sucesso);
        var resumo = (await new EfEstoqueInsumoConsultaRepository(seed).ConsultarInsumoAsync(material.Id, Hoje))!.Resumo;
        Assert.Equal(3m, resumo.SaldoDisponivel);
        Assert.True(resumo.SaldoFisico >= resumo.SaldoReservado);
    }

    private static async Task<bool> ReservarAsync(XaquiamuniDbContext db, OrdemProducao ordem)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var sucesso = await Repositorio(db).PrepararReservaProducaoAsync(ordem, default);
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        db.ChangeTracker.Clear();
        return sucesso;
    }

    private static async Task<ReceitaProducao> SemearAsync(XaquiamuniDbContext db,
        params (InsumoEstoque Material, decimal Quantidade)[] itens)
    {
        var categoria = Categoria.Criar("Sabonetes", null, 0);
        var produto = Produto.CriarSimples("Sabonete", null, categoria, "UN", 10m);
        var receita = ReceitaProducao.Criar(produto.Variantes.Single(), "Receita", 1,
            itens.Select(i => ItemReceitaProducao.Criar(i.Material, i.Quantidade, Grama)).ToArray(), Autor, Agora);
        db.Categorias.Add(categoria);
        db.Produtos.Add(produto);
        db.InsumosEstoque.AddRange(itens.Select(i => i.Material));
        db.ReceitasProducao.Add(receita);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return receita;
    }

    private static async Task<MovimentoInsumo> EntradaAsync(EfEstoqueInsumoRepository repo, InsumoEstoque material,
        decimal quantidade, string? lote = null, DateOnly? validade = null, bool bloqueado = false, DateOnly? entradaEm = null)
    {
        var resultado = await repo.RegistrarEntradaAsync(new(material.Id, Autor, TipoMovimentoInsumo.EstoqueInicial,
            quantidade, Grama, null, null, lote, lote is null ? null : entradaEm ?? Hoje, validade, null, bloqueado, null, Chave()));
        Assert.True(resultado.Sucesso, resultado.Erro);
        return resultado.Movimento!;
    }

    private static InsumoEstoque Material(string nome, bool lote = false) =>
        InsumoEstoque.Criar(nome, TipoInsumoEstoque.MateriaPrima, Grama, controlaLote: lote);

    private static string Chave() => Guid.NewGuid().ToString("D");

    private static EfEstoqueInsumoRepository Repositorio(XaquiamuniDbContext db)
    {
        var relogio = new RelogioFixo();
        return new(db, relogio, new CalendarioOperacionalEstoque(relogio, Options.Create(new CalendarioOperacionalEstoqueOptions())));
    }

    private sealed class RelogioFixo : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Agora;
    }
}
