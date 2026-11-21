using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Financeiro;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class CadeiaFabricacaoSqlServerIntegrationTests
{
    private const string Lancamentos = Financeiro + "/lancamentos";
    private const string Pendencias = Lancamentos + "/pendentes";
    private static readonly DateOnly VencimentoConta = new(2026, 11, 22);
    private static readonly DateOnly DataQuitacao = new(2026, 1, 10);

    [SqlServerIntegrationFact]
    public async Task Contas_PosicaoECaixaConciliaQuitacaoCancelamentoEReplay()
    {
        await using var ambiente = await Ambiente.CriarAsync();
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        using var cliente = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Cliente);
        var entrada = new CriarLancamentoPendenteInput("Entrada", "Serviços", "Receber", VencimentoConta, 2000,
            " Empresa ", " Contrato ");
        var saida = entrada with { Tipo = "Saida", Descricao = "Pagar", ValorCentavos = 700 };
        using var semData = await admin.EnviarAsync(Pendencias, entrada with { Vencimento = null }, Guid.NewGuid());
        using var zero = await admin.EnviarAsync(Pendencias, entrada with { ValorCentavos = 0 }, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.BadRequest, semData.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);
        var chaveCriar = Guid.NewGuid();
        var receber = await admin.PostAsync<LancamentoDto>(Pendencias, entrada, HttpStatusCode.Created, chaveCriar);
        var pagar = await admin.PostAsync<LancamentoDto>(Pendencias, saida, HttpStatusCode.Created, Guid.NewGuid());
        Assert.Equal("Pendente", receber.Situacao);
        Assert.Equal("Empresa", receber.Contraparte);
        Assert.Equal("Contrato", receber.Observacoes);
        Assert.Equal(VencimentoConta, receber.Vencimento);
        Assert.Null(receber.Data);
        Assert.Null(receber.Meio);
        Assert.Null(receber.QuitadoPorId);
        Assert.Equal(pagar, Assert.Single((await admin.Http
            .GetFromJsonAsync<LancamentoDto[]>(Lancamentos + "?situacao=Pendente&tipo=Saida"))!));
        await ConferirTentativaAsync(admin, chaveCriar, "criar-pendente", receber.Id, receber);

        var resumo = await admin.Http.GetFromJsonAsync<ResumoFinanceiroDto>(Financeiro + "/resumo");
        Assert.Equal(("2000", "700", "0", "0"), (resumo!.PosicaoAtual.AReceberCentavos, resumo.PosicaoAtual.APagarCentavos,
            resumo.FluxoPeriodo.EntradasRecebidasCentavos, resumo.FluxoPeriodo.SaidasPagasCentavos));
        var quitar = $"{Lancamentos}/{receber.Id:D}/quitar";
        var dados = new QuitarLancamentoInput(DataQuitacao, "Pix");
        using var proibida = await cliente.EnviarAsync(quitar, dados, Guid.NewGuid());
        using var semCsrf = await admin.Http.PostAsJsonAsync(quitar, dados);
        using var futuro = await admin.EnviarAsync(quitar, dados with { Data = DateOnly.MaxValue }, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Forbidden, proibida.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, semCsrf.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, futuro.StatusCode);

        var chaves = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var respostas = await Task.WhenAll(chaves.Select(chave => admin.EnviarAsync(quitar, dados, chave)));
        using var vencedora = Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.OK);
        using var perdedora = Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.Conflict);
        var realizado = (await vencedora.Content.ReadFromJsonAsync<LancamentoDto>())!;
        var chaveQuitacao = chaves[Array.IndexOf(respostas, vencedora)];
        Assert.Equal(("Realizado", receber.Id, DataQuitacao, receber.ResponsavelId),
            (realizado.Situacao, realizado.Id, realizado.Data!.Value, realizado.QuitadoPorId!.Value));
        Assert.Equal(TimeSpan.Zero, realizado.QuitadoEmUtc!.Value.Offset);
        await ConferirTentativaAsync(admin, chaveQuitacao, "quitar", receber.Id, realizado);
        Assert.Equal(realizado, await admin.PostAsync<LancamentoDto>(quitar, dados, HttpStatusCode.OK, chaveQuitacao));
        using var divergente = await admin.EnviarAsync(quitar, dados with { Meio = "Dinheiro" }, chaveQuitacao);
        using var cancelarQuitada = await admin.EnviarAsync($"{Lancamentos}/{receber.Id:D}/cancelar",
            new CancelarLancamentoInput("Ajuste"), Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict, divergente.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, cancelarQuitada.StatusCode);

        var cancelada = await admin.PostAsync<LancamentoDto>($"{Lancamentos}/{pagar.Id:D}/cancelar",
            new CancelarLancamentoInput("Obrigação cancelada"), HttpStatusCode.OK, Guid.NewGuid());
        Assert.Equal("Cancelado", cancelada.Situacao);
        Assert.Null(cancelada.Data);
        using var quitarCancelada = await admin.EnviarAsync($"{Lancamentos}/{pagar.Id:D}/quitar", dados, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict, quitarCancelada.StatusCode);

        var caixa = await admin.Http.GetFromJsonAsync<PaginaMovimentacoesFinanceiras>(Financeiro +
            "/movimentacoes?dataInicial=2026-01-10&dataFinal=2026-01-10");
        var movimento = Assert.Single(caixa!.Itens);
        Assert.Equal((receber.Id, "2000", DataQuitacao, "LancamentoManual"),
            (movimento.EventoId, movimento.ValorCentavos, movimento.Data, movimento.Origem));
        var final = await admin.Http.GetFromJsonAsync<ResumoFinanceiroDto>(Financeiro + "/resumo");
        Assert.Equal(("0", "0", "2000", "0"), (final!.PosicaoAtual.AReceberCentavos, final.PosicaoAtual.APagarCentavos,
            final.FluxoPeriodo.EntradasRecebidasCentavos, final.FluxoPeriodo.SaidasPagasCentavos));
    }

    [SqlServerIntegrationFact]
    public async Task Contas_MigrationPreservaRegistrosEReplayLegado()
    {
        await using var ambiente = await Ambiente.CriarAsync("20261121101248_RegistrarPrecoAceitoPedido");
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var autor = (await admin.Http.GetFromJsonAsync<JsonElement>("/api/admin/auth/me")).GetProperty("userId").GetGuid();
        var antigo = Guid.NewGuid();
        var cancelado = Guid.NewGuid();
        var chave = Guid.NewGuid();
        var criadoEm = new DateTimeOffset(2026, 1, 10, 18, 0, 0, TimeSpan.Zero);
        var entrada = new CriarLancamentoInput("Entrada", "Outros", "Legado", DataQuitacao, "Pix", 2300);
        var input = new { entrada.Tipo, entrada.Categoria, entrada.Descricao, entrada.Data, entrada.Meio, entrada.ValorCentavos };
        var impressao = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new { operacao = "criar", id = (Guid?)null, input })));
        var snapshot = JsonSerializer.Serialize(new { Id = antigo, entrada.Tipo, entrada.Categoria, entrada.Descricao,
            entrada.Data, entrada.Meio, ValorCentavos = "2300", ResponsavelId = autor, CriadoEmUtc = criadoEm });
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [LancamentosFinanceiros]
                ([Id],[Tipo],[Categoria],[Descricao],[ValorCentavos],[Data],[Meio],[ResponsavelId],[CriadoEmUtc],
                 [CanceladoPorId],[CanceladoEmUtc],[MotivoCancelamento])
            VALUES ({antigo}, 'Entrada','Outros','Legado',2300,{DataQuitacao},'Pix',{autor},{criadoEm}, NULL,NULL,NULL),
                   ({cancelado}, 'Saida','Outros','Estava cancelado',500,{DataQuitacao},'Pix',{autor},{criadoEm},
                    {autor},{criadoEm},'Ajuste');
            INSERT INTO [OperacoesFinanceiras] ([ResponsavelId],[Chave],[Impressao],[Resultado])
            VALUES ({autor},{chave},{impressao},{snapshot});
            """);
        await db.Database.MigrateAsync();
        var atual = await admin.Http.GetFromJsonAsync<LancamentoDto>($"{Lancamentos}/{antigo:D}");
        Assert.Equal("Realizado", atual!.Situacao);
        Assert.Null(atual.QuitadoEmUtc);
        Assert.Null(atual.QuitadoPorId);
        Assert.Equal(antigo, atual.Id);
        Assert.Equal(atual, await admin.PostAsync<LancamentoDto>(Lancamentos, entrada, HttpStatusCode.OK, chave));
        await ConferirTentativaAsync(admin, chave, "criar", antigo, atual);
        var historico = await admin.Http.GetFromJsonAsync<LancamentoDto>($"{Lancamentos}/{cancelado:D}");
        Assert.Equal(("Cancelado", "Ajuste", criadoEm), (historico!.Situacao, historico.MotivoCancelamento, historico.CanceladoEmUtc));
        var correcao = await admin.PostAsync<LancamentoDto>($"{Lancamentos}/{antigo:D}/cancelar",
            new CancelarLancamentoInput("Correção legada"), HttpStatusCode.OK, Guid.NewGuid());
        Assert.Equal("Cancelado", correcao.Situacao);
    }

    [SqlServerIntegrationFact]
    public async Task Contas_FalhaNaGravacaoDaQuitacaoPreservaPendencia()
    {
        await using var ambiente = await Ambiente.CriarAsync();
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var conta = await admin.PostAsync<LancamentoDto>(Pendencias,
            new CriarLancamentoPendenteInput("Saida", "Outros", "Conta", VencimentoConta, 500),
            HttpStatusCode.Created, Guid.NewGuid());
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE [OperacoesFinanceiras] ADD CONSTRAINT [CK_FalhaQuitacaoTeste] CHECK ([Operacao] <> 'quitar');
            """);
        var url = $"{Lancamentos}/{conta.Id:D}/quitar";
        var chave = Guid.NewGuid();
        var input = new QuitarLancamentoInput(DataQuitacao, "Boleto");
        using var falhou = await admin.EnviarAsync(url, input, chave);
        Assert.Equal(HttpStatusCode.InternalServerError, falhou.StatusCode);
        Assert.Equal(conta, await admin.Http.GetFromJsonAsync<LancamentoDto>($"{Lancamentos}/{conta.Id:D}"));
        using var tentativa = await admin.Http.GetAsync(Financeiro + $"/tentativas/{chave:D}");
        Assert.Equal(HttpStatusCode.NotFound, tentativa.StatusCode);
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE [OperacoesFinanceiras] DROP CONSTRAINT [CK_FalhaQuitacaoTeste]");
        var quitada = await admin.PostAsync<LancamentoDto>(url, input, HttpStatusCode.OK, chave);
        var caixa = await admin.Http.GetFromJsonAsync<PaginaMovimentacoesFinanceiras>(Financeiro + "/movimentacoes");
        Assert.Equal((quitada.Id, "Saida", "500"), (Assert.Single(caixa!.Itens).EventoId, caixa.Itens[0].Tipo, caixa.Itens[0].ValorCentavos));
    }
}
