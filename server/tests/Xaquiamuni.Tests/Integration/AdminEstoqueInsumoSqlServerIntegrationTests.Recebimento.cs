using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Api.Estoque;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class AdminEstoqueInsumoSqlServerIntegrationTests
{
    private const string RotaCompras = "/api/admin/compras-materias-primas";
    private static readonly DateTimeOffset AgoraRecebimento = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);

    [SqlServerIntegrationFact]
    public async Task RecebimentoCompra_ReverteUltimoItemESerializaEntradasComReplayELotes()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        using var factory = CreateFactory(database, new AdjustableTimeProvider(AgoraRecebimento));
        using var admin = CreateClient(factory, XaquiamuniRoles.Administrador);
        using var anonimo = CreateClient(factory);
        using var cliente = CreateClient(factory, XaquiamuniRoles.Cliente);
        var csrf = await GetCsrfAsync(admin);

        var fornecedor = Fornecedor.Criar("Ervas do Vale", null, null, null, AgoraRecebimento);
        var extrato = InsumoEstoque.Criar("Extrato", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Mililitro);
        var frasco = extrato.AdicionarFormaCompra("Frasco 100 ml", 100m, UnidadeFisicaEstoque.Mililitro);
        var ervas = new[] { "Arruda Macho", "Arruda Fêmea", "Alecrim", "Alevante", "Espada de São Jorge", "Guiné", "Comigo-ninguém-pode" }
            .Select((nome, i) => InsumoEstoque.Criar(nome, TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama,
                controlaLote: i is 5 or 6, validadeObrigatoria: i == 6)).ToArray();
        await using (var db = database.CreateVerificationContext())
        {
            db.Fornecedores.Add(fornecedor);
            db.InsumosEstoque.Add(extrato);
            db.InsumosEstoque.AddRange(ervas);
            await db.SaveChangesAsync();
        }

        var compra = await CriarRascunhoRecebimentoAsync(admin, csrf, fornecedor.Id, [
            new(null, 2, 0, [new(extrato.Id, frasco.Id, 2, null)]),
            new(null, 1, null, [new(extrato.Id, null, 1, null)]),
            new("Sete ervas", 1, 24.75m, ervas.Select((e, i) => new ComponenteCompraInput(e.Id, null, i + 0.5m, null)).ToArray()),
        ]);
        var url = $"{RotaCompras}/{compra.Resumo.Id:D}/receber";
        var chave = Guid.NewGuid();
        using var semSessao = await anonimo.PostAsJsonAsync(url, new { versao = compra.Versao });
        using var proibido = await cliente.PostAsJsonAsync(url, new { versao = compra.Versao });
        using var semCsrf = await admin.PostAsJsonAsync(url, new { versao = compra.Versao });
        Assert.Equal(HttpStatusCode.Unauthorized, semSessao.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, proibido.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, semCsrf.StatusCode);
        using var semChave = await EnviarRecebimentoAsync(admin, csrf, url, new { versao = compra.Versao });
        Assert.Equal(HttpStatusCode.BadRequest, semChave.StatusCode);
        using var versaoErrada = await EnviarRecebimentoAsync(admin, csrf, url, new { versao = Guid.NewGuid() }, chave);
        Assert.Equal(HttpStatusCode.Conflict, versaoErrada.StatusCode);

        using var falhou = await EnviarRecebimentoAsync(admin, csrf, url, new { versao = compra.Versao }, chave);
        Assert.Equal(HttpStatusCode.BadRequest, falhou.StatusCode);
        Assert.Contains("validade", await falhou.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        await using (var db = database.CreateVerificationContext())
        {
            Assert.False(await db.SaldosInsumo.AnyAsync());
            Assert.False(await db.MovimentosInsumo.AnyAsync());
            Assert.False(await db.LotesInsumo.AnyAsync());
            Assert.False(await db.EntradasRecebimentoCompra.AnyAsync());
            Assert.Equal(SituacaoCompraMateriaPrima.Rascunho, (await db.ComprasMateriasPrimas.SingleAsync()).Situacao);
        }

        await using (var db = database.CreateVerificationContext())
        {
            var material = await db.InsumosEstoque.Include(i => i.FormasCompra).SingleAsync(i => i.Id == extrato.Id);
            material.AtualizarFormaCompra(frasco.Id, "Novo nome do frasco", 100m, UnidadeFisicaEstoque.Mililitro);
            await db.SaveChangesAsync();
        }

        var ultimo = compra.Linhas[2].Componentes[6].Id;
        var dataValidade = new DateOnly(2026, 11, 25);
        var dados = new ReceberCompraMateriaPrimaInput(compra.Versao, [new(ultimo, dataValidade)]);
        using var avulsaRequest = CreateEntryRequest($"{AdminEstoqueInsumoEndpoints.BasePath}/{extrato.Id:D}/entradas",
            new EntradaInsumoAdminRequest
            {
                Tipo = TipoMovimentoInsumo.Aquisicao,
                QuantidadeOriginal = 3,
                UnidadeOriginal = UnidadeFisicaEstoque.Mililitro,
            }, csrf, "entrada-avulsa-paralela");

        var respostas = await Task.WhenAll(
            EnviarRecebimentoAsync(admin, csrf, url, dados, chave),
            EnviarRecebimentoAsync(admin, csrf, url, dados, chave),
            admin.SendAsync(avulsaRequest));
        using var primeira = respostas[0];
        using var repetida = respostas[1];
        using var avulsa = respostas[2];
        Assert.Equal(HttpStatusCode.OK, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.OK, repetida.StatusCode);
        Assert.Equal(await primeira.Content.ReadAsStringAsync(), await repetida.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Created, avulsa.StatusCode);

        using var chaveDivergente = await EnviarRecebimentoAsync(admin, csrf, url,
            dados with { Validades = [new(ultimo, dataValidade.AddDays(1))] }, chave);
        Assert.Equal(HttpStatusCode.Conflict, chaveDivergente.StatusCode);
        using var outraChave = await EnviarRecebimentoAsync(admin, csrf, url, dados, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict, outraChave.StatusCode);
        using var cancelarRecebida = await EnviarRecebimentoAsync(admin, csrf,
            $"{RotaCompras}/{compra.Resumo.Id:D}/cancelar", new { versao = compra.Versao });
        Assert.Equal(HttpStatusCode.Conflict, cancelarRecebida.StatusCode);

        await using var verificacao = database.CreateVerificationContext();
        var recebido = await verificacao.ComprasMateriasPrimas.SingleAsync();
        Assert.Equal(SituacaoCompraMateriaPrima.Recebida, recebido.Situacao);
        Assert.Equal(AgoraRecebimento, recebido.RecebidaEmUtc);
        Assert.Equal(AdminId, recebido.RecebidaPorId);
        Assert.Equal(fornecedor.Nome, recebido.NomeFornecedor);
        Assert.Null(recebido.Total);
        var vinculos = await verificacao.EntradasRecebimentoCompra.OrderBy(e => e.Ordem).ToArrayAsync();
        Assert.Equal(9, vinculos.Length);
        Assert.Equal(compra.Linhas.SelectMany(l => l.Componentes.Select(c => c.Id)), vinculos.Select(v => v.ComponenteId));
        var movimentos = await verificacao.MovimentosInsumo.ToDictionaryAsync(m => m.Id);
        Assert.Equal(10, movimentos.Count);
        Assert.Equal(0m, movimentos[vinculos[0].MovimentoId].CustoTotal);
        Assert.Equal(0m, movimentos[vinculos[0].MovimentoId].CustoPorUnidadeBase);
        Assert.Equal("Frasco 100 ml", movimentos[vinculos[0].MovimentoId].FormaCompraNome);
        Assert.All(vinculos.Skip(1), item =>
        {
            Assert.Null(movimentos[item.MovimentoId].CustoTotal);
            Assert.Null(movimentos[item.MovimentoId].CustoPorUnidadeBase);
        });
        var saldo = await verificacao.SaldosInsumo.SingleAsync(s => s.InsumoId == extrato.Id);
        Assert.Equal(204m, saldo.QuantidadeFisica.ValorBase);
        Assert.Null(saldo.LoteId);
        var lotes = await verificacao.LotesInsumo.ToDictionaryAsync(l => l.InsumoId);
        Assert.Equal(2, lotes.Count);
        Assert.Null(lotes[ervas[5].Id].DataValidade);
        Assert.Equal(dataValidade, lotes[ervas[6].Id].DataValidade);
        Assert.Equal(fornecedor.Nome, lotes[ervas[6].Id].Fornecedor);
        using var estorno = await EnviarRecebimentoAsync(admin, csrf,
            $"{AdminEstoqueInsumoEndpoints.BasePath}/{extrato.Id:D}/movimentacoes/{vinculos[0].MovimentoId:D}/estorno",
            new EstornoInsumoAdminRequest { Motivo = "Estorno avulso" }, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict, estorno.StatusCode);
        Assert.Contains("vinculadas", await estorno.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [SqlServerIntegrationFact]
    public async Task RecebimentoCompra_BloqueiaCanceladaInativoEConversaoDivergente()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        using var factory = CreateFactory(database, new AdjustableTimeProvider(AgoraRecebimento));
        using var admin = CreateClient(factory, XaquiamuniRoles.Administrador);
        var csrf = await GetCsrfAsync(admin);
        var fornecedor = Fornecedor.Criar("Fornecedor", null, null, null, AgoraRecebimento);
        var insumo = InsumoEstoque.Criar("Extrato", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Mililitro);
        var forma = insumo.AdicionarFormaCompra("Frasco 100 ml", 100m, UnidadeFisicaEstoque.Mililitro);
        await using (var db = database.CreateVerificationContext())
        {
            db.Fornecedores.Add(fornecedor);
            db.InsumosEstoque.Add(insumo);
            await db.SaveChangesAsync();
        }
        LinhaCompraInput[] linhas = [new(null, 1, 18m, [new(insumo.Id, forma.Id, 1, null)])];
        var cancelada = await CriarRascunhoRecebimentoAsync(admin, csrf, fornecedor.Id, linhas);
        var rascunho = await CriarRascunhoRecebimentoAsync(admin, csrf, fornecedor.Id, linhas);
        using var cancelar = await EnviarRecebimentoAsync(admin, csrf, $"{RotaCompras}/{cancelada.Resumo.Id:D}/cancelar",
            new { versao = cancelada.Versao });
        Assert.Equal(HttpStatusCode.OK, cancelar.StatusCode);
        var canceladaAtual = await cancelar.Content.ReadFromJsonAsync<AdminComprasMateriasPrimasEndpoints.DetalheCompra>();
        using var recusaCancelada = await EnviarRecebimentoAsync(admin, csrf, $"{RotaCompras}/{cancelada.Resumo.Id:D}/receber",
            new { versao = canceladaAtual!.Versao }, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict, recusaCancelada.StatusCode);

        await using (var db = database.CreateVerificationContext())
        {
            var material = await db.InsumosEstoque.Include(i => i.FormasCompra).SingleAsync();
            material.AtualizarFormaCompra(forma.Id, "Frasco 125 ml", 125m, UnidadeFisicaEstoque.Mililitro);
            await db.SaveChangesAsync();
        }
        var url = $"{RotaCompras}/{rascunho.Resumo.Id:D}/receber";
        using var recusaConversao = await EnviarRecebimentoAsync(admin, csrf, url, new { versao = rascunho.Versao }, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict, recusaConversao.StatusCode);
        Assert.Contains("rascunho", await recusaConversao.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        await using (var db = database.CreateVerificationContext())
        {
            var material = await db.InsumosEstoque.SingleAsync();
            material.DefinirAtivo(false);
            await db.SaveChangesAsync();
        }
        using var inativo = await EnviarRecebimentoAsync(admin, csrf, url, new { versao = rascunho.Versao }, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict, inativo.StatusCode);
        await using var verificacao = database.CreateVerificationContext();
        Assert.False(await verificacao.MovimentosInsumo.AnyAsync());
        Assert.False(await verificacao.EntradasRecebimentoCompra.AnyAsync());
        var salvo = await verificacao.ComprasMateriasPrimas.Include(c => c.Linhas).ThenInclude(l => l.Componentes)
            .SingleAsync(c => c.Id == rascunho.Resumo.Id);
        Assert.Equal(SituacaoCompraMateriaPrima.Rascunho, salvo.Situacao);
        Assert.Equal(100m, Assert.Single(Assert.Single(salvo.Linhas).Componentes).QuantidadeBase);

        var reativado = await verificacao.InsumosEstoque.SingleAsync();
        reativado.DefinirAtivo(true);
        await verificacao.SaveChangesAsync();
        using var revisao = new HttpRequestMessage(HttpMethod.Put, $"{RotaCompras}/{rascunho.Resumo.Id:D}")
        {
            Content = JsonContent.Create(new CompraMateriaPrimaInput(fornecedor.Id, new(2026, 11, 19), linhas, rascunho.Versao))
        };
        AddAntiforgery(revisao, csrf);
        using var revista = await admin.SendAsync(revisao);
        Assert.Equal(HttpStatusCode.OK, revista.StatusCode);
        var atual = await revista.Content.ReadFromJsonAsync<AdminComprasMateriasPrimasEndpoints.DetalheCompra>();
        using var recebido = await EnviarRecebimentoAsync(admin, csrf, url, new { versao = atual!.Versao }, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, recebido.StatusCode);
        var movimento = await verificacao.MovimentosInsumo.AsNoTracking().SingleAsync();
        Assert.Equal(125m, movimento.QuantidadeBase.ValorBase);
        Assert.Equal(18m, movimento.CustoTotal);
        Assert.Equal("Frasco 125 ml", movimento.FormaCompraNome);
    }

    private static async Task<AdminComprasMateriasPrimasEndpoints.DetalheCompra> CriarRascunhoRecebimentoAsync(
        HttpClient admin, CsrfState csrf, Guid fornecedorId, LinhaCompraInput[] linhas)
    {
        using var resposta = await EnviarRecebimentoAsync(admin, csrf, RotaCompras,
            new CompraMateriaPrimaInput(fornecedorId, new(2026, 11, 19), linhas, null), Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        return Assert.IsType<AdminComprasMateriasPrimasEndpoints.DetalheCompra>(
            await resposta.Content.ReadFromJsonAsync<AdminComprasMateriasPrimasEndpoints.DetalheCompra>());
    }

    private static async Task<HttpResponseMessage> EnviarRecebimentoAsync(HttpClient admin, CsrfState csrf,
        string url, object corpo, Guid? chave = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(corpo) };
        AddAntiforgery(request, csrf);
        if (chave.HasValue) request.Headers.Add("Idempotency-Key", chave.Value.ToString("D"));
        return await admin.SendAsync(request);
    }
}
