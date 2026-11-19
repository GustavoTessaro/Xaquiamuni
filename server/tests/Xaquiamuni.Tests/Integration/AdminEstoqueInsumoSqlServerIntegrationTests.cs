using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xaquiamuni.Api.Autenticacao;
using Xaquiamuni.Api.Estoque;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Identidade;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

[Trait("Category", "SqlServerIntegration")]
public sealed class AdminEstoqueInsumoSqlServerIntegrationTests
{
    private static readonly Guid AdminId = Guid.Parse("cd4d0df3-43be-4e42-9ba9-a8da1d03ef29");
    private static readonly DateOnly DataOperacional = new(2026, 11, 16);

    [SqlServerIntegrationFact]
    public async Task AdminStockEndpoints_EnforceRoleAndAntiforgery_AndCreateWithoutOpeningBalance()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        using var factory = CreateFactory(database);

        using (var anonymous = CreateClient(factory))
        using (var response = await anonymous.GetAsync(AdminEstoqueInsumoEndpoints.BasePath))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using (var clientUser = CreateClient(factory, XaquiamuniRoles.Cliente))
        using (var response = await clientUser.GetAsync(AdminEstoqueInsumoEndpoints.BasePath))
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        using var admin = CreateClient(factory, XaquiamuniRoles.Administrador);
        var csrf = await GetCsrfAsync(admin);
        var path = AdminEstoqueInsumoEndpoints.BasePath;
        using (var noTokenRequest = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(CreateSupplyRequest())
        })
        using (var noTokenResponse = await admin.SendAsync(noTokenRequest))
        {
            Assert.Equal(HttpStatusCode.BadRequest, noTokenResponse.StatusCode);
        }

        using (var clientWrite = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(CreateSupplyRequest())
        })
        {
            AddAntiforgery(clientWrite, csrf);
            using var forbiddenWrite = await SendAsClientAsync(factory, clientWrite, csrf);
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenWrite.StatusCode);
        }

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(CreateSupplyRequest())
        };
        AddAntiforgery(createRequest, csrf);
        using var createResponse = await admin.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(createResponse.Headers.Location);
        var created = await createResponse.Content.ReadFromJsonAsync<DetalheInsumoAdminResponse>();
        var supply = Assert.IsType<DetalheInsumoAdminResponse>(created);
        Assert.Equal("Cera de teste", supply.Resumo.Nome);
        Assert.False(Assert.Single(supply.Posicoes).TemRegistroSaldo);

        using var detailResponse = await admin.GetAsync(createResponse.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content.ReadFromJsonAsync<DetalheInsumoAdminResponse>();
        Assert.False(Assert.Single(Assert.IsType<DetalheInsumoAdminResponse>(detail).Posicoes).TemRegistroSaldo);

        using var verification = database.CreateVerificationContext();
        Assert.False(await verification.SaldosInsumo.AnyAsync(
            item => item.InsumoId == supply.Resumo.InsumoId));
        Assert.False(await verification.MovimentosInsumo.AnyAsync(
            item => item.InsumoId == supply.Resumo.InsumoId));
    }

    [SqlServerIntegrationFact]
    public async Task ComprasMateriasPrimas_RascunhoProtegidoComSnapshotsEConcorrenciaSemMovimentarEstoque()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        using var factory = CreateFactory(database);
        const string path = "/api/admin/compras-materias-primas";
        using var anonimo = CreateClient(factory);
        using var cliente = CreateClient(factory, XaquiamuniRoles.Cliente);
        using var admin = CreateClient(factory, XaquiamuniRoles.Administrador);
        using var semSessao = await anonimo.GetAsync(path);
        using var proibido = await cliente.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, semSessao.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, proibido.StatusCode);
        using var semCsrf = await admin.PostAsJsonAsync(path, new { });
        Assert.Equal(HttpStatusCode.BadRequest, semCsrf.StatusCode);
        var csrf = await GetCsrfAsync(admin);

        var fornecedor = Xaquiamuni.Dominio.Entidades.Fornecedor.Criar("Ervas do Vale", null, null, null, DateTimeOffset.UtcNow);
        var extrato = InsumoEstoque.Criar("Extrato", TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Mililitro);
        var frasco = extrato.AdicionarFormaCompra("Frasco 100 ml", 100, UnidadeFisicaEstoque.Mililitro);
        var ervas = new[] { "Arruda Macho", "Arruda Fêmea", "Alecrim", "Alevante", "Espada de São Jorge", "Guiné", "Comigo-ninguém-pode" }
            .Select(n => InsumoEstoque.Criar(n, TipoInsumoEstoque.MateriaPrima, UnidadeFisicaEstoque.Grama)).ToArray();
        await using (var db = database.CreateVerificationContext())
        {
            db.Fornecedores.Add(fornecedor);
            db.InsumosEstoque.Add(extrato);
            db.InsumosEstoque.AddRange(ervas);
            await db.SaveChangesAsync();
        }

        var data = new DateOnly(2026, 11, 19);
        var chave = Guid.NewGuid();
        Xaquiamuni.Aplicacao.Estoque.CompraMateriaPrimaInput Montar(Guid? versao = null, Guid? snapshot = null,
            decimal? preco = 23.45m, Guid? apresentacao = null) =>
            new(fornecedor.Id, data, [
                new(null, 2, 0, [new(extrato.Id, apresentacao ?? frasco.Id, 2, snapshot)]),
                new("Sete ervas", 1, preco, ervas.Select((e, i) =>
                    new Xaquiamuni.Aplicacao.Estoque.ComponenteCompraInput(e.Id, null, i + 0.5m, null)).ToArray())
            ], versao);
        async Task<HttpResponseMessage> Enviar(HttpMethod metodo, string url, object corpo, Guid? key = null)
        {
            using var request = new HttpRequestMessage(metodo, url) { Content = JsonContent.Create(corpo) };
            AddAntiforgery(request, csrf);
            if (key.HasValue) request.Headers.Add("Idempotency-Key", key.ToString());
            return await admin.SendAsync(request);
        }

        using var formaInvalida = await Enviar(HttpMethod.Post, path, Montar(apresentacao: Guid.NewGuid()), Guid.NewGuid());
        Assert.Equal(HttpStatusCode.BadRequest, formaInvalida.StatusCode);

        var gravacoes = await Task.WhenAll(Enviar(HttpMethod.Post, path, Montar(), chave), Enviar(HttpMethod.Post, path, Montar(), chave));
        using var primeira = Assert.Single(gravacoes, r => r.StatusCode == HttpStatusCode.Created);
        using var repetida = Assert.Single(gravacoes, r => r.StatusCode == HttpStatusCode.OK);
        var url = Assert.IsType<Uri>(primeira.Headers.Location).ToString();
        var criado = await primeira.Content.ReadFromJsonAsync<JsonElement>();
        var replay = await repetida.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(criado.GetProperty("resumo").GetProperty("id").GetGuid(), replay.GetProperty("resumo").GetProperty("id").GetGuid());
        Assert.Equal("23.45", criado.GetProperty("resumo").GetProperty("total").GetString());
        var linhas = criado.GetProperty("linhas");
        Assert.Equal("0", linhas[0].GetProperty("valorTotal").GetString());
        var item = linhas[0].GetProperty("componentes")[0];
        Assert.Equal("200", item.GetProperty("quantidadeBase").GetString());
        Assert.Equal(7, linhas[1].GetProperty("componentes").GetArrayLength());
        Assert.False(linhas[1].GetProperty("componentes")[0].TryGetProperty("valorTotal", out _));
        using var chaveDivergente = await Enviar(HttpMethod.Post, path, Montar(preco: 25m), chave);
        Assert.Equal(HttpStatusCode.Conflict, chaveDivergente.StatusCode);

        var versao = criado.GetProperty("versao").GetGuid();
        var snapshot = item.GetProperty("id").GetGuid();
        await using (var db = database.CreateVerificationContext())
        {
            var material = await db.InsumosEstoque.Include(i => i.FormasCompra).SingleAsync(i => i.Id == extrato.Id);
            material.AtualizarFormaCompra(frasco.Id, "Frasco 125 ml", 125, UnidadeFisicaEstoque.Mililitro);
            var f = await db.Fornecedores.SingleAsync();
            f.Editar("Outro nome", null, null, null, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        var edicoes = await Task.WhenAll(
            Enviar(HttpMethod.Put, url, Montar(versao, snapshot, null)),
            Enviar(HttpMethod.Put, url, Montar(versao, snapshot, null)));
        using var sucesso = Assert.Single(edicoes, r => r.StatusCode == HttpStatusCode.OK);
        using var obsoleta = Assert.Single(edicoes, r => r.StatusCode == HttpStatusCode.Conflict);
        var editado = await sucesso.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, editado.GetProperty("resumo").GetProperty("total").ValueKind);
        Assert.Equal("Ervas do Vale", editado.GetProperty("resumo").GetProperty("nomeFornecedor").GetString());
        Assert.Equal("Frasco 100 ml", editado.GetProperty("linhas")[0].GetProperty("componentes")[0].GetProperty("nomeApresentacao").GetString());

        await using (var db = database.CreateVerificationContext())
        {
            var material = await db.InsumosEstoque.SingleAsync(i => i.Id == extrato.Id);
            material.DefinirAtivo(false);
            await db.SaveChangesAsync();
        }
        using var materialInativo = await Enviar(HttpMethod.Post, path, Montar(), Guid.NewGuid());
        Assert.Equal(HttpStatusCode.BadRequest, materialInativo.StatusCode);
        await using (var db = database.CreateVerificationContext())
        {
            var f = await db.Fornecedores.SingleAsync();
            f.DefinirAtivo(false, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }
        using var fornecedorInativo = await Enviar(HttpMethod.Post, path, Montar(), Guid.NewGuid());
        Assert.Equal(HttpStatusCode.BadRequest, fornecedorInativo.StatusCode);
        var novaVersao = editado.GetProperty("versao").GetGuid();
        var novoSnapshot = editado.GetProperty("linhas")[0].GetProperty("componentes")[0].GetProperty("id").GetGuid();
        using var preservar = await Enviar(HttpMethod.Put, url, Montar(novaVersao, novoSnapshot, 25m));
        Assert.Equal(HttpStatusCode.OK, preservar.StatusCode);
        var versaoFinal = (await preservar.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("versao").GetGuid();
        using var cancelada = await Enviar(HttpMethod.Post, url + "/cancelar", new { versao = versaoFinal });
        Assert.Equal(HttpStatusCode.OK, cancelada.StatusCode);
        using var cancelamentoRepetido = await Enviar(HttpMethod.Post, url + "/cancelar", new { versao = versaoFinal });
        Assert.Equal(HttpStatusCode.OK, cancelamentoRepetido.StatusCode);
        using var edicaoCancelada = await Enviar(HttpMethod.Put, url, Montar(versaoFinal, novoSnapshot));
        Assert.Equal(HttpStatusCode.Conflict, edicaoCancelada.StatusCode);
        using var detalhe = await admin.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, detalhe.StatusCode);
        Assert.Single((await admin.GetFromJsonAsync<JsonElement[]>(path + "?situacao=Cancelada&busca=Vale"))!);

        await using var verificacao = database.CreateVerificationContext();
        Assert.Equal(1, await verificacao.ComprasMateriasPrimas.CountAsync());
        Assert.False(await verificacao.SaldosInsumo.AnyAsync());
        Assert.False(await verificacao.MovimentosInsumo.AnyAsync());
        Assert.False(await verificacao.LotesInsumo.AnyAsync());
    }

    [SqlServerIntegrationFact]
    public async Task AdminFornecedores_PersisteCadastroEdicaoEInativacaoComAdminECsrf()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        using var factory = CreateFactory(database);
        const string path = "/api/admin/fornecedores";
        using var anonimo = CreateClient(factory);
        using var cliente = CreateClient(factory, XaquiamuniRoles.Cliente);
        using var admin = CreateClient(factory, XaquiamuniRoles.Administrador);
        using var semSessao = await anonimo.GetAsync(path);
        using var proibido = await cliente.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, semSessao.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, proibido.StatusCode);

        using var semCsrf = await admin.PostAsJsonAsync(path, new { nome = "Vale" });
        Assert.Equal(HttpStatusCode.BadRequest, semCsrf.StatusCode);
        var csrf = await GetCsrfAsync(admin);
        async Task<HttpResponseMessage> Enviar(HttpMethod metodo, string url, object corpo)
        {
            using var request = new HttpRequestMessage(metodo, url) { Content = JsonContent.Create(corpo) };
            AddAntiforgery(request, csrf);
            return await admin.SendAsync(request);
        }

        using var invalido = await Enviar(HttpMethod.Post, path, new { nome = " " });
        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);
        using var criado = await Enviar(HttpMethod.Post, path, new { nome = " Ervas  do Vale " });
        Assert.Equal(HttpStatusCode.Created, criado.StatusCode);
        var url = Assert.IsType<Uri>(criado.Headers.Location).ToString();
        using var duplicado = await Enviar(HttpMethod.Post, path, new { nome = "ERVAS DO VALE" });
        Assert.Equal(HttpStatusCode.Conflict, duplicado.StatusCode);
        using var editado = await Enviar(HttpMethod.Put, url, new
        {
            nome = "Ervas da Serra", telefone = "+55 (11) 90000-0000", email = "teste@example.test",
            observacoes = "Entrega semanal"
        });
        Assert.Equal(HttpStatusCode.OK, editado.StatusCode);
        using var inativado = await Enviar(HttpMethod.Patch, url + "/status", new { ativo = false });
        Assert.Equal(HttpStatusCode.OK, inativado.StatusCode);
        using var detalhe = await admin.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, detalhe.StatusCode);
        using var json = JsonDocument.Parse(await detalhe.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.GetProperty("ativo").GetBoolean());
        Assert.Equal("Ervas da Serra", json.RootElement.GetProperty("nome").GetString());
        Assert.Empty((await admin.GetFromJsonAsync<JsonElement[]>(path + "?ativo=true"))!);
        Assert.Single((await admin.GetFromJsonAsync<JsonElement[]>(path + "?busca=serra&ativo=false"))!);

        await using var db = database.CreateVerificationContext();
        var persistido = await db.Fornecedores.SingleAsync();
        Assert.Equal("teste@example.test", persistido.Email);
        Assert.Equal("Entrega semanal", persistido.Observacoes);
        Assert.False(persistido.Ativo);
        Assert.Equal(TimeSpan.Zero, persistido.CriadoEmUtc.Offset);
        Assert.True(persistido.AtualizadoEmUtc >= persistido.CriadoEmUtc);
        Assert.False(await db.MovimentosInsumo.AnyAsync());
    }

    [SqlServerIntegrationFact]
    public async Task AdminStockWrites_UseAuthenticatedActor_PreserveSnapshotsAndRejectStaleCounts()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        using var factory = CreateFactory(
            database,
            new AdjustableTimeProvider(new DateTimeOffset(2026, 11, 16, 15, 0, 0, TimeSpan.Zero)));
        using var admin = CreateClient(factory, XaquiamuniRoles.Administrador);
        var csrf = await GetCsrfAsync(admin);
        var basePath = AdminEstoqueInsumoEndpoints.BasePath;

        var supply = await CreateSupplyAsync(
            admin,
            csrf,
            "Cera em lotes",
            TipoInsumoEstoque.MateriaPrima,
            UnidadeFisicaEstoque.Grama,
            controlaLote: true,
            validadeObrigatoria: true,
            estoqueMinimo: 50m);
        var supplyId = supply.Resumo.InsumoId;

        using var formsListResponse = await admin.GetAsync($"{basePath}/{supplyId:D}/formas-compra");
        Assert.Equal(HttpStatusCode.OK, formsListResponse.StatusCode);

        using var formRequest = new HttpRequestMessage(HttpMethod.Post, $"{basePath}/{supplyId:D}/formas-compra")
        {
            Content = JsonContent.Create(new FormaCompraAdminRequest
            {
                Nome = "Pacote de um quilo",
                QuantidadeEquivalentePorEmbalagem = 1m,
                Unidade = UnidadeFisicaEstoque.Quilograma
            })
        };
        AddAntiforgery(formRequest, csrf);
        using var formResponse = await admin.SendAsync(formRequest);
        Assert.Equal(HttpStatusCode.Created, formResponse.StatusCode);
        var form = Assert.IsType<FormaCompraAdminResponse>(
            await formResponse.Content.ReadFromJsonAsync<FormaCompraAdminResponse>());
        Assert.Equal(1000m, form.QuantidadeEquivalentePorEmbalagem);
        Assert.Equal(UnidadeFisicaEstoque.Grama, form.Unidade);

        using var initialEntry = CreateEntryRequest(
            $"{basePath}/{supplyId:D}/entradas",
            new
            {
                tipo = "EstoqueInicial",
                responsavelId = Guid.NewGuid(),
                quantidadeOriginal = 1000m,
                unidadeOriginal = "Grama",
                identificacaoNovoLote = "LOTE-HTTP-01",
                dataEntradaNovoLote = DataOperacional,
                dataValidadeNovoLote = DataOperacional
            },
            csrf,
            "http-initial");
        using var initialResponse = await admin.SendAsync(initialEntry);
        Assert.Equal(HttpStatusCode.Created, initialResponse.StatusCode);
        var initial = Assert.IsType<MovimentoAdminResponse>(
            await initialResponse.Content.ReadFromJsonAsync<MovimentoAdminResponse>());
        Assert.Equal(AdminId, initial.ResponsavelId);

        var lotId = Assert.IsType<Guid>(initial.LoteId);
        using var acquisition = CreateEntryRequest(
            $"{basePath}/{supplyId:D}/entradas",
            new EntradaInsumoAdminRequest
            {
                Tipo = TipoMovimentoInsumo.Aquisicao,
                QuantidadeOriginal = 1m,
                FormaCompraId = form.Id,
                LoteId = lotId,
                CustoTotal = 2.50m
            },
            csrf,
            "http-acquisition");
        using var acquisitionResponse = await admin.SendAsync(acquisition);
        Assert.Equal(HttpStatusCode.Created, acquisitionResponse.StatusCode);
        var acquisitionMovement = Assert.IsType<MovimentoAdminResponse>(
            await acquisitionResponse.Content.ReadFromJsonAsync<MovimentoAdminResponse>());
        Assert.Equal(1000m, acquisitionMovement.FatorCompra);
        Assert.Equal(UnidadeFisicaEstoque.Grama, acquisitionMovement.UnidadeFatorCompra);
        Assert.Equal(2.50m, acquisitionMovement.CustoTotal);
        Assert.Equal(AdminId, acquisitionMovement.ResponsavelId);

        using var movementDetail = await admin.GetAsync(
            $"{basePath}/{supplyId:D}/movimentacoes/{acquisitionMovement.Id:D}");
        Assert.Equal(HttpStatusCode.OK, movementDetail.StatusCode);

        using var updateForm = new HttpRequestMessage(
            HttpMethod.Put,
            $"{basePath}/{supplyId:D}/formas-compra/{form.Id:D}")
        {
            Content = JsonContent.Create(new FormaCompraAdminRequest
            {
                Nome = "Pacote de meio quilo",
                QuantidadeEquivalentePorEmbalagem = 500m,
                Unidade = UnidadeFisicaEstoque.Grama
            })
        };
        AddAntiforgery(updateForm, csrf);
        using var updateFormResponse = await admin.SendAsync(updateForm);
        Assert.Equal(HttpStatusCode.OK, updateFormResponse.StatusCode);

        using var replayRequest = CreateEntryRequest(
            $"{basePath}/{supplyId:D}/entradas",
            new EntradaInsumoAdminRequest
            {
                Tipo = TipoMovimentoInsumo.Aquisicao,
                QuantidadeOriginal = 1m,
                FormaCompraId = form.Id,
                LoteId = lotId,
                CustoTotal = 2.50m
            },
            csrf,
            "http-acquisition");
        using var replayResponse = await admin.SendAsync(replayRequest);
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        var replay = Assert.IsType<MovimentoAdminResponse>(
            await replayResponse.Content.ReadFromJsonAsync<MovimentoAdminResponse>());
        Assert.Equal(acquisitionMovement.Id, replay.Id);
        Assert.Equal(1000m, replay.FatorCompra);

        using var conflictRequest = CreateEntryRequest(
            $"{basePath}/{supplyId:D}/entradas",
            new EntradaInsumoAdminRequest
            {
                Tipo = TipoMovimentoInsumo.Aquisicao,
                QuantidadeOriginal = 2m,
                FormaCompraId = form.Id,
                LoteId = lotId,
                CustoTotal = 2.50m
            },
            csrf,
            "http-acquisition");
        using var conflictResponse = await admin.SendAsync(conflictRequest);
        Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);

        using var invalidEntryRequest = CreateEntryRequest(
            $"{basePath}/{supplyId:D}/entradas",
            new EntradaInsumoAdminRequest
            {
                Tipo = TipoMovimentoInsumo.Aquisicao,
                QuantidadeOriginal = 0m,
                UnidadeOriginal = UnidadeFisicaEstoque.Grama,
                LoteId = lotId
            },
            csrf,
            "http-invalid-entry");
        using var invalidEntryResponse = await admin.SendAsync(invalidEntryRequest);
        Assert.Equal(HttpStatusCode.BadRequest, invalidEntryResponse.StatusCode);

        using var movementHistory = await admin.GetAsync(
            $"{basePath}/{supplyId:D}/movimentacoes?tipo=Aquisicao&pagina=1&itensPorPagina=10");
        Assert.Equal(HttpStatusCode.OK, movementHistory.StatusCode);
        var history = Assert.IsType<PaginaAdminResponse<MovimentoAdminResponse>>(
            await movementHistory.Content.ReadFromJsonAsync<
                PaginaAdminResponse<MovimentoAdminResponse>>());
        var historicalAcquisition = Assert.Single(history.Itens);
        Assert.Equal(1000m, historicalAcquisition.FatorCompra);
        Assert.Equal("Pacote de um quilo", historicalAcquisition.FormaCompraNome);

        using var lotsResponse = await admin.GetAsync($"{basePath}/{supplyId:D}/lotes");
        Assert.Equal(HttpStatusCode.OK, lotsResponse.StatusCode);
        var lots = Assert.IsType<PosicaoEstoqueAdminResponse[]>(
            await lotsResponse.Content.ReadFromJsonAsync<PosicaoEstoqueAdminResponse[]>());
        var lot = Assert.Single(lots);
        Assert.NotNull(lot.VersaoSaldo);

        using var countRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"{basePath}/{supplyId:D}/contagens")
        {
            Content = JsonContent.Create(new ContagemInsumoAdminRequest
            {
                QuantidadeEncontrada = 1800m,
                UnidadeOriginal = UnidadeFisicaEstoque.Grama,
                LoteId = lotId,
                VersaoSaldoEsperada = lot.VersaoSaldo,
                Motivo = "Contagem de teste"
            })
        };
        countRequest.Headers.Add("Idempotency-Key", "http-count");
        AddAntiforgery(countRequest, csrf);
        using var countResponse = await admin.SendAsync(countRequest);
        Assert.Equal(HttpStatusCode.Created, countResponse.StatusCode);

        using var staleCountRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"{basePath}/{supplyId:D}/contagens")
        {
            Content = JsonContent.Create(new ContagemInsumoAdminRequest
            {
                QuantidadeEncontrada = 1700m,
                UnidadeOriginal = UnidadeFisicaEstoque.Grama,
                LoteId = lotId,
                VersaoSaldoEsperada = lot.VersaoSaldo,
                Motivo = "Contagem obsoleta"
            })
        };
        staleCountRequest.Headers.Add("Idempotency-Key", "http-count-stale");
        AddAntiforgery(staleCountRequest, csrf);
        using var staleCountResponse = await admin.SendAsync(staleCountRequest);
        Assert.Equal(HttpStatusCode.Conflict, staleCountResponse.StatusCode);

        using var ordinaryUpdate = new HttpRequestMessage(HttpMethod.Put, $"{basePath}/{supplyId:D}")
        {
            Content = JsonContent.Create(new AtualizarInsumoAdminRequest
            {
                Nome = "Cera em lotes atualizada",
                Tipo = TipoInsumoEstoque.MateriaPrima,
                ControlaLote = true,
                ValidadeObrigatoria = true,
                EstoqueMinimo = 60m
            })
        };
        AddAntiforgery(ordinaryUpdate, csrf);
        using var ordinaryUpdateResponse = await admin.SendAsync(ordinaryUpdate);
        Assert.Equal(HttpStatusCode.OK, ordinaryUpdateResponse.StatusCode);

        using var updatePolicy = new HttpRequestMessage(HttpMethod.Put, $"{basePath}/{supplyId:D}")
        {
            Content = JsonContent.Create(new AtualizarInsumoAdminRequest
            {
                Nome = "Cera alterada",
                Tipo = TipoInsumoEstoque.MateriaPrima,
                ControlaLote = false,
                ValidadeObrigatoria = false,
                EstoqueMinimo = 50m
            })
        };
        AddAntiforgery(updatePolicy, csrf);
        using var policyResponse = await admin.SendAsync(updatePolicy);
        Assert.Equal(HttpStatusCode.Conflict, policyResponse.StatusCode);

        using var exitRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"{basePath}/{supplyId:D}/saidas")
        {
            Content = JsonContent.Create(new SaidaInsumoAdminRequest
            {
                Tipo = TipoMovimentoInsumo.SaidaManual,
                QuantidadeOriginal = 100m,
                UnidadeOriginal = UnidadeFisicaEstoque.Grama,
                LoteId = lotId,
                Motivo = "Uso de teste"
            })
        };
        exitRequest.Headers.Add("Idempotency-Key", "http-exit");
        AddAntiforgery(exitRequest, csrf);
        using var exitResponse = await admin.SendAsync(exitRequest);
        Assert.Equal(HttpStatusCode.Created, exitResponse.StatusCode);
        var exitMovement = Assert.IsType<MovimentoAdminResponse>(
            await exitResponse.Content.ReadFromJsonAsync<MovimentoAdminResponse>());

        using var insufficientExitRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"{basePath}/{supplyId:D}/saidas")
        {
            Content = JsonContent.Create(new SaidaInsumoAdminRequest
            {
                Tipo = TipoMovimentoInsumo.SaidaManual,
                QuantidadeOriginal = 10_000m,
                UnidadeOriginal = UnidadeFisicaEstoque.Grama,
                LoteId = lotId,
                Motivo = "Quantidade acima do saldo"
            })
        };
        insufficientExitRequest.Headers.Add("Idempotency-Key", "http-exit-insufficient");
        AddAntiforgery(insufficientExitRequest, csrf);
        using var insufficientExitResponse = await admin.SendAsync(insufficientExitRequest);
        Assert.Equal(HttpStatusCode.Conflict, insufficientExitResponse.StatusCode);

        using var reversalRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"{basePath}/{supplyId:D}/movimentacoes/{exitMovement.Id:D}/estorno")
        {
            Content = JsonContent.Create(new EstornoInsumoAdminRequest
            {
                Motivo = "Estorno de teste"
            })
        };
        reversalRequest.Headers.Add("Idempotency-Key", "http-reversal");
        AddAntiforgery(reversalRequest, csrf);
        using var reversalResponse = await admin.SendAsync(reversalRequest);
        Assert.Equal(HttpStatusCode.Created, reversalResponse.StatusCode);

        using var replayReversalRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"{basePath}/{supplyId:D}/movimentacoes/{exitMovement.Id:D}/estorno")
        {
            Content = JsonContent.Create(new EstornoInsumoAdminRequest
            {
                Motivo = "Estorno de teste"
            })
        };
        replayReversalRequest.Headers.Add("Idempotency-Key", "http-reversal");
        AddAntiforgery(replayReversalRequest, csrf);
        using var replayReversalResponse = await admin.SendAsync(replayReversalRequest);
        Assert.Equal(HttpStatusCode.OK, replayReversalResponse.StatusCode);

        using var duplicateReversalRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"{basePath}/{supplyId:D}/movimentacoes/{exitMovement.Id:D}/estorno")
        {
            Content = JsonContent.Create(new EstornoInsumoAdminRequest
            {
                Motivo = "Estorno duplicado"
            })
        };
        duplicateReversalRequest.Headers.Add("Idempotency-Key", "http-reversal-duplicate");
        AddAntiforgery(duplicateReversalRequest, csrf);
        using var duplicateReversalResponse = await admin.SendAsync(duplicateReversalRequest);
        Assert.Equal(HttpStatusCode.Conflict, duplicateReversalResponse.StatusCode);

        using var inactiveSupplyRequest = new HttpRequestMessage(
            HttpMethod.Patch,
            $"{basePath}/{supplyId:D}/status")
        {
            Content = JsonContent.Create(new StatusInsumoAdminRequest { Ativo = false })
        };
        AddAntiforgery(inactiveSupplyRequest, csrf);
        using var inactiveSupplyResponse = await admin.SendAsync(inactiveSupplyRequest);
        Assert.Equal(HttpStatusCode.OK, inactiveSupplyResponse.StatusCode);
        var inactiveSupply = Assert.IsType<DetalheInsumoAdminResponse>(
            await inactiveSupplyResponse.Content.ReadFromJsonAsync<DetalheInsumoAdminResponse>());
        Assert.False(inactiveSupply.Resumo.Ativo);

        using var statusRequest = new HttpRequestMessage(
            HttpMethod.Patch,
            $"{basePath}/{supplyId:D}/formas-compra/{form.Id:D}/status")
        {
            Content = JsonContent.Create(new StatusFormaCompraAdminRequest { Ativa = false })
        };
        AddAntiforgery(statusRequest, csrf);
        using var statusResponse = await admin.SendAsync(statusRequest);
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);

        using var verification = database.CreateVerificationContext();
        Assert.Equal(1, await verification.MovimentosInsumo.CountAsync(
            item => item.InsumoId == supplyId
                && item.ResponsavelId == AdminId
                && item.Tipo == TipoMovimentoInsumo.Aquisicao));
        Assert.Equal(1800m, await verification.SaldosInsumo
            .Where(item => item.InsumoId == supplyId && item.LoteId == lotId)
            .Select(item => item.QuantidadeFisica.ValorBase)
            .SingleAsync());
    }

    [SqlServerIntegrationFact]
    public async Task AdminAcquisitionWithoutCost_PreservesUnknownCostAndIsIdempotent()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        using var factory = CreateFactory(database);
        using var admin = CreateClient(factory, XaquiamuniRoles.Administrador);
        var csrf = await GetCsrfAsync(admin);
        var basePath = AdminEstoqueInsumoEndpoints.BasePath;
        var supply = await CreateSupplyAsync(
            admin,
            csrf,
            "Pigmento sem custo conhecido",
            TipoInsumoEstoque.MateriaPrima,
            UnidadeFisicaEstoque.Grama);
        var supplyId = supply.Resumo.InsumoId;
        var requestBody = new
        {
            tipo = "Aquisicao",
            quantidadeOriginal = 2.123456m,
            unidadeOriginal = "Grama"
        };

        using var request = CreateEntryRequest(
            $"{basePath}/{supplyId:D}/entradas",
            requestBody,
            csrf,
            "http-acquisition-unknown-cost");
        using var response = await admin.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var movement = Assert.IsType<MovimentoAdminResponse>(
            await response.Content.ReadFromJsonAsync<MovimentoAdminResponse>());
        Assert.Equal(2.123456m, movement.QuantidadeBase);
        Assert.Null(movement.CustoTotal);
        Assert.Null(movement.CustoPorUnidadeBase);

        using var positionResponse = await admin.GetAsync($"{basePath}/{supplyId:D}/posicao");
        Assert.Equal(HttpStatusCode.OK, positionResponse.StatusCode);
        var position = Assert.IsType<PosicaoInsumoAdminResponse>(
            await positionResponse.Content.ReadFromJsonAsync<PosicaoInsumoAdminResponse>());
        Assert.Equal(2.123456m, position.Resumo.SaldoFisico);
        Assert.Equal(2.123456m, position.Resumo.SaldoUtilizavel);
        Assert.Equal(2.123456m, position.Resumo.SaldoDisponivel);

        using var historyResponse = await admin.GetAsync(
            $"{basePath}/{supplyId:D}/movimentacoes?tipo=Aquisicao&pagina=1&itensPorPagina=10");
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        var history = Assert.IsType<PaginaAdminResponse<MovimentoAdminResponse>>(
            await historyResponse.Content.ReadFromJsonAsync<
                PaginaAdminResponse<MovimentoAdminResponse>>());
        var historicalMovement = Assert.Single(history.Itens);
        Assert.Equal(movement.Id, historicalMovement.Id);
        Assert.Null(historicalMovement.CustoTotal);
        Assert.Null(historicalMovement.CustoPorUnidadeBase);

        using var replayRequest = CreateEntryRequest(
            $"{basePath}/{supplyId:D}/entradas",
            requestBody,
            csrf,
            "http-acquisition-unknown-cost");
        using var replayResponse = await admin.SendAsync(replayRequest);
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        var replay = Assert.IsType<MovimentoAdminResponse>(
            await replayResponse.Content.ReadFromJsonAsync<MovimentoAdminResponse>());
        Assert.Equal(movement.Id, replay.Id);
        Assert.Null(replay.CustoTotal);

        using var verification = database.CreateVerificationContext();
        Assert.Equal(1, await verification.MovimentosInsumo.CountAsync(
            item => item.InsumoId == supplyId && item.Tipo == TipoMovimentoInsumo.Aquisicao));
        var storedMovement = await verification.MovimentosInsumo.SingleAsync(
            item => item.InsumoId == supplyId && item.Tipo == TipoMovimentoInsumo.Aquisicao);
        Assert.Null(storedMovement.CustoTotal);
        Assert.Null(storedMovement.CustoPorUnidadeBase);
        Assert.Equal(2.123456m, await verification.SaldosInsumo
            .Where(item => item.InsumoId == supplyId)
            .Select(item => item.QuantidadeFisica.ValorBase)
            .SingleAsync());
    }

    [SqlServerIntegrationFact]
    public async Task OperationalCalendar_UsesSaoPauloDateAtMidnightUtcBoundary()
    {
        var clock = new AdjustableTimeProvider(new DateTimeOffset(2026, 11, 17, 2, 59, 59, TimeSpan.Zero));
        await using var database = await SqlServerTestDatabase.CreateAsync();
        using var factory = CreateFactory(database, clock);
        using var admin = CreateClient(factory, XaquiamuniRoles.Administrador);
        var csrf = await GetCsrfAsync(admin);
        var path = AdminEstoqueInsumoEndpoints.BasePath;

        var supply = await CreateSupplyAsync(
            admin,
            csrf,
            "Óleo essencial",
            TipoInsumoEstoque.MateriaPrima,
            UnidadeFisicaEstoque.Mililitro,
            controlaLote: true,
            validadeObrigatoria: true);
        var supplyId = supply.Resumo.InsumoId;

        using var initial = CreateEntryRequest(
            $"{path}/{supplyId:D}/entradas",
            new EntradaInsumoAdminRequest
            {
                Tipo = TipoMovimentoInsumo.EstoqueInicial,
                QuantidadeOriginal = 3m,
                UnidadeOriginal = UnidadeFisicaEstoque.Mililitro,
                IdentificacaoNovoLote = "LOTE-VALIDADE",
                DataEntradaNovoLote = DataOperacional,
                DataValidadeNovoLote = DataOperacional
            },
            csrf,
            "expiry-initial");
        using var initialResponse = await admin.SendAsync(initial);
        Assert.Equal(HttpStatusCode.Created, initialResponse.StatusCode);
        var movement = Assert.IsType<MovimentoAdminResponse>(
            await initialResponse.Content.ReadFromJsonAsync<MovimentoAdminResponse>());
        var lotId = Assert.IsType<Guid>(movement.LoteId);

        using var beforeExpiryRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"{path}/{supplyId:D}/saidas")
        {
            Content = JsonContent.Create(new SaidaInsumoAdminRequest
            {
                Tipo = TipoMovimentoInsumo.SaidaManual,
                QuantidadeOriginal = 1m,
                UnidadeOriginal = UnidadeFisicaEstoque.Mililitro,
                LoteId = lotId,
                Motivo = "Uso antes do fim do vencimento"
            })
        };
        beforeExpiryRequest.Headers.Add("Idempotency-Key", "expiry-before");
        AddAntiforgery(beforeExpiryRequest, csrf);
        using var beforeExpiry = await admin.SendAsync(beforeExpiryRequest);
        Assert.Equal(HttpStatusCode.Created, beforeExpiry.StatusCode);

        clock.SetUtcNow(new DateTimeOffset(2026, 11, 17, 3, 0, 0, TimeSpan.Zero));

        using var afterExpiryRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"{path}/{supplyId:D}/saidas")
        {
            Content = JsonContent.Create(new SaidaInsumoAdminRequest
            {
                Tipo = TipoMovimentoInsumo.SaidaManual,
                QuantidadeOriginal = 1m,
                UnidadeOriginal = UnidadeFisicaEstoque.Mililitro,
                LoteId = lotId,
                Motivo = "Tentativa após vencimento"
            })
        };
        afterExpiryRequest.Headers.Add("Idempotency-Key", "expiry-after");
        AddAntiforgery(afterExpiryRequest, csrf);
        using var afterExpiry = await admin.SendAsync(afterExpiryRequest);
        Assert.Equal(HttpStatusCode.Conflict, afterExpiry.StatusCode);

        using var positionBeforeDiscardResponse = await admin.GetAsync($"{path}/{supplyId:D}/posicao");
        var positionBeforeDiscard = Assert.IsType<PosicaoInsumoAdminResponse>(
            await positionBeforeDiscardResponse.Content.ReadFromJsonAsync<PosicaoInsumoAdminResponse>());
        Assert.Equal(2m, positionBeforeDiscard.Resumo.SaldoFisico);
        Assert.Equal(0m, positionBeforeDiscard.Resumo.SaldoUtilizavel);

        using var lossRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"{path}/{supplyId:D}/saidas")
        {
            Content = JsonContent.Create(new SaidaInsumoAdminRequest
            {
                Tipo = TipoMovimentoInsumo.Perda,
                QuantidadeOriginal = 1m,
                UnidadeOriginal = UnidadeFisicaEstoque.Mililitro,
                LoteId = lotId,
                Motivo = "Perda após vencimento"
            })
        };
        lossRequest.Headers.Add("Idempotency-Key", "expiry-loss");
        AddAntiforgery(lossRequest, csrf);
        using var loss = await admin.SendAsync(lossRequest);
        Assert.Equal(HttpStatusCode.Created, loss.StatusCode);

        using var discardRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"{path}/{supplyId:D}/saidas")
        {
            Content = JsonContent.Create(new SaidaInsumoAdminRequest
            {
                Tipo = TipoMovimentoInsumo.Descarte,
                QuantidadeOriginal = 1m,
                UnidadeOriginal = UnidadeFisicaEstoque.Mililitro,
                LoteId = lotId,
                Motivo = "Descarte após vencimento"
            })
        };
        discardRequest.Headers.Add("Idempotency-Key", "expiry-discard");
        AddAntiforgery(discardRequest, csrf);
        using var discard = await admin.SendAsync(discardRequest);
        Assert.Equal(HttpStatusCode.Created, discard.StatusCode);

        using var positionResponse = await admin.GetAsync($"{path}/{supplyId:D}/posicao");
        var position = Assert.IsType<PosicaoInsumoAdminResponse>(
            await positionResponse.Content.ReadFromJsonAsync<PosicaoInsumoAdminResponse>());
        Assert.Equal(0m, position.Resumo.SaldoFisico);
        Assert.Equal(0m, position.Resumo.SaldoUtilizavel);
        Assert.Equal(0m, position.Resumo.SaldoDisponivel);
    }

    private static CadastrarInsumoAdminRequest CreateSupplyRequest() =>
        new()
        {
            Nome = "Cera de teste",
            Tipo = TipoInsumoEstoque.MateriaPrima,
            UnidadeBase = UnidadeFisicaEstoque.Grama
        };

    private static async Task<DetalheInsumoAdminResponse> CreateSupplyAsync(
        HttpClient client,
        CsrfState csrf,
        string name,
        TipoInsumoEstoque type,
        UnidadeFisicaEstoque baseUnit,
        bool controlaLote = false,
        bool validadeObrigatoria = false,
        decimal? estoqueMinimo = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, AdminEstoqueInsumoEndpoints.BasePath)
        {
            Content = JsonContent.Create(new CadastrarInsumoAdminRequest
            {
                Nome = name,
                Tipo = type,
                UnidadeBase = baseUnit,
                EstoqueMinimo = estoqueMinimo,
                ControlaLote = controlaLote,
                ValidadeObrigatoria = validadeObrigatoria
            })
        };
        AddAntiforgery(request, csrf);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Assert.IsType<DetalheInsumoAdminResponse>(
            await response.Content.ReadFromJsonAsync<DetalheInsumoAdminResponse>());
    }

    private static HttpRequestMessage CreateEntryRequest(
        string path,
        object body,
        CsrfState csrf,
        string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        AddAntiforgery(request, csrf);
        return request;
    }

    private static WebApplicationFactory<Program> CreateFactory(
        SqlServerTestDatabase database,
        AdjustableTimeProvider? clock = null)
    {
        using var context = database.CreateVerificationContext();
        var connectionString = context.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The isolated SQL test connection string is unavailable.");

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:SqlServer"] = connectionString,
                    ["Estoque:Calendario:TimeZoneId"] = "America/Sao_Paulo",
                    ["EmailOutboxWorker:Enabled"] = "false",
                    ["ResendEmail:Enabled"] = "false"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.AddDbContext<XaquiamuniDbContext>(options => options.UseSqlServer(connectionString));
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                if (clock is not null)
                {
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(clock);
                }

                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthenticationHandler.TestSchemeName;
                        options.DefaultChallengeScheme = TestAuthenticationHandler.TestSchemeName;
                        options.DefaultForbidScheme = TestAuthenticationHandler.TestSchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.TestSchemeName,
                        _ => { });
            });
        });
    }

    private static HttpClient CreateClient(
        WebApplicationFactory<Program> factory,
        string? role = null)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false
        });
        if (role is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthenticationHandler.RoleHeader, role);
            client.DefaultRequestHeaders.Add(TestAuthenticationHandler.UserIdHeader, AdminId.ToString("D"));
        }

        return client;
    }

    private static async Task<CsrfState> GetCsrfAsync(HttpClient client)
    {
        using var response = await client.GetAsync(AntiforgeryConfiguration.TokenEndpoint);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var requestToken = Assert.IsType<string>(document.RootElement.GetProperty("requestToken").GetString());
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        return new CsrfState(requestToken, cookie.Split(';', 2)[0]);
    }

    private static void AddAntiforgery(HttpRequestMessage request, CsrfState csrf)
    {
        request.Headers.Add(AntiforgeryConfiguration.HeaderName, csrf.RequestToken);
        request.Headers.Add("Cookie", csrf.CookiePair);
    }

    private static async Task<HttpResponseMessage> SendAsClientAsync(
        WebApplicationFactory<Program> factory,
        HttpRequestMessage request,
        CsrfState csrf)
    {
        using var client = CreateClient(factory, XaquiamuniRoles.Cliente);
        request.Headers.Remove("Cookie");
        request.Headers.Remove(AntiforgeryConfiguration.HeaderName);
        AddAntiforgery(request, csrf);
        return await client.SendAsync(request);
    }

    private sealed record CsrfState(string RequestToken, string CookiePair);

    private sealed class AdjustableTimeProvider(DateTimeOffset currentTime) : TimeProvider
    {
        private DateTimeOffset _currentTime = currentTime.ToUniversalTime();

        public override DateTimeOffset GetUtcNow() => _currentTime;

        public void SetUtcNow(DateTimeOffset value) => _currentTime = value.ToUniversalTime();
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string TestSchemeName = "AdminStockHttpIntegrationTest";
        public const string RoleHeader = "X-Test-Role";
        public const string UserIdHeader = "X-Test-User-Id";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(RoleHeader, out var role)
                || !Request.Headers.TryGetValue(UserIdHeader, out var userId)
                || !Guid.TryParse(userId, out var parsedUserId))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, parsedUserId.ToString("D")),
                    new Claim(ClaimTypes.Role, role.ToString())
                ],
                TestSchemeName);
            return Task.FromResult(
                AuthenticateResult.Success(
                    new AuthenticationTicket(new ClaimsPrincipal(identity), TestSchemeName)));
        }
    }
}
