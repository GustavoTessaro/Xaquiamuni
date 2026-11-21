using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;
using Xaquiamuni.Aplicacao.Financeiro;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Tests.Integration;

public sealed partial class CadeiaFabricacaoSqlServerIntegrationTests
{
    [SqlServerIntegrationFact]
    public async Task ComprovantePix_ValidaAcessoFormatosConcorrenciaReplayEIntegridadeComercial()
    {
        await using var ambiente = await Ambiente.CriarAsync();
        using var admin = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Administrador);
        using var cliente = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Cliente);
        using var outro = await Sessao.EntrarAsync(ambiente.Host, XaquiamuniRoles.Cliente);
        await using var db = ambiente.Banco.CreateVerificationContext();
        var (produto, variante, _) = await ReservarProdutoAcabadoSqlServerIntegrationTests.CriarProdutoCompravelAsync(db);
        await admin.PostAsync<JsonElement>($"/api/admin/estoque/produtos-acabados/variantes/{variante.Id:D}/entradas",
            new { tipo = "EntradaManual", quantidade = 4, condicaoDestino = "Vendavel", semControleValidade = true, motivo = "Teste" },
            HttpStatusCode.Created, Guid.NewGuid());
        var carrinho = new { itens = new[] { new { produtoId = produto.Id, produtoVarianteId = variante.Id, quantidade = 1 } } };
        var checkout = Guid.NewGuid();
        var criado = await cliente.PostAsync<JsonElement>(Pedidos, carrinho, HttpStatusCode.Created, checkout);
        var id = criado.GetProperty("id").GetGuid();
        var rota = $"/api/pedidos/{id:D}/comprovante-pix";
        var rotaAdmin = $"/api/admin/pedidos/{id:D}/comprovante-pix";
        var antes = await db.Pedidos.AsNoTracking().SingleAsync();
        var reservaAntes = await db.ReservasProdutoAcabado.AsNoTracking().SingleAsync();
        var movimentosAntes = await db.MovimentosProdutoAcabado.CountAsync();
        var token = await TokenComprovanteAsync(cliente);
        var png = ImagemComprovante(SKEncodedImageFormat.Png);
        var jpg = ImagemComprovante(SKEncodedImageFormat.Jpeg);
        var pdf = PdfComprovante();

        using var semCsrf = await EnviarComprovanteAsync(cliente, rota, png, "recibo.png", "image/png", null);
        Assert.Equal(HttpStatusCode.BadRequest, semCsrf.StatusCode);
        using var indevido = await EnviarComprovanteAsync(outro, rota, png, "recibo.png", "image/png", await TokenComprovanteAsync(outro));
        Assert.Equal(HttpStatusCode.NotFound, indevido.StatusCode);
        using var foraDoPainel = await cliente.Http.GetAsync(rotaAdmin);
        Assert.Equal(HttpStatusCode.Forbidden, foraDoPainel.StatusCode);
        foreach (var (bytes, nome, mime) in new[]
        {
            (png, "incompativel.jpg", "image/png"),
            (png, "assinatura.jpg", "image/jpeg"),
            (new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, "truncado.png", "image/png"),
            (jpg[..^2], "truncado.jpg", "image/jpeg"),
            (PdfComprovante("/OpenAction << /S /JavaScript >>"), "ativo.pdf", "application/pdf"),
            (pdf[..^7], "truncado.pdf", "application/pdf"),
        })
        {
            using var invalido = await EnviarComprovanteAsync(cliente, rota, bytes, nome, mime, token);
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, invalido.StatusCode);
        }

        using var grande = await EnviarComprovanteAsync(cliente, rota, new byte[5 * 1024 * 1024 + 1], "grande.pdf", "application/pdf", token);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, grande.StatusCode);
        using var malformado = await EnviarComprovanteAsync(cliente, rota, png, "recibo.png", "image/png", token, duplicar: true);
        Assert.Equal(HttpStatusCode.BadRequest, malformado.StatusCode);

        var chave = Guid.NewGuid();
        var concorrentes = await Task.WhenAll(
            EnviarComprovanteAsync(cliente, rota, pdf, "nome-pessoal.pdf", "application/pdf", token, chave),
            EnviarComprovanteAsync(cliente, rota, jpg, "foto.jpg", "image/jpeg", token));
        using var primeiro = concorrentes[0];
        using var segundo = concorrentes[1];
        Assert.Equal(new[] { 201, 409 }, concorrentes.Select(r => (int)r.StatusCode).Order().ToArray());
        var pdfGanhou = primeiro.StatusCode == HttpStatusCode.Created;
        var original = (await (pdfGanhou ? primeiro : segundo).Content.ReadFromJsonAsync<ComprovantePixDto>())!;
        var outroArquivo = pdfGanhou ? jpg : pdf;
        var nomeNovo = pdfGanhou ? "foto.jpeg" : "recibo.pdf";
        var mimeNovo = pdfGanhou ? "image/jpeg" : "application/pdf";
        using var receber = await outro.Http.GetAsync(rota + "/conteudo");
        Assert.Equal(HttpStatusCode.NotFound, receber.StatusCode);
        Assert.Equal(original, await admin.Http.GetFromJsonAsync<ComprovantePixDto>(rotaAdmin));
        using var download = await admin.Http.GetAsync(rotaAdmin + "/conteudo");
        Assert.Equal(pdfGanhou ? pdf : jpg, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal(original.Tipo, download.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
        Assert.StartsWith("comprovante-pix.", download.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal("nosniff", Assert.Single(download.Headers.GetValues("X-Content-Type-Options")));
        Assert.True(download.Headers.CacheControl is { Private: true, NoStore: true });

        using var substituir = await EnviarComprovanteAsync(cliente, rota, outroArquivo, nomeNovo, mimeNovo, token, versao: original.Versao);
        Assert.Equal(HttpStatusCode.Created, substituir.StatusCode);
        var atual = (await substituir.Content.ReadFromJsonAsync<ComprovantePixDto>())!;
        using var antiga = await EnviarComprovanteAsync(cliente, rota, png, "scan.png", "image/png", token, versao: original.Versao);
        Assert.Equal(HttpStatusCode.Conflict, antiga.StatusCode);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER FalhaComprovanteTeste ON ComprovantesPixAtivos AFTER UPDATE AS
            THROW 51017, 'Falha deliberada de teste.', 1;
            """);
        var chavePng = Guid.NewGuid();
        try
        {
            using var falha = await EnviarComprovanteAsync(cliente, rota, png, "scan.png", "image/png", token, chavePng, atual.Versao);
            Assert.Equal(HttpStatusCode.InternalServerError, falha.StatusCode);
            Assert.Equal(atual, await cliente.Http.GetFromJsonAsync<ComprovantePixDto>(rota));
            Assert.Equal(outroArquivo, await cliente.Http.GetByteArrayAsync(rota + "/conteudo"));
            Assert.Equal(2, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM EnviosComprovantePix").SingleAsync());
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER FalhaComprovanteTeste");
        }

        // Descarta a resposta após os cabeçalhos de sucesso, sem ler o resultado.
        using (var respostaPerdida = await EnviarComprovanteAsync(cliente, rota, png, "scan.png", "image/png", token, chavePng, atual.Versao))
            Assert.Equal(HttpStatusCode.Created, respostaPerdida.StatusCode);
        var final = (await cliente.Http.GetFromJsonAsync<ComprovantePixDto>(rota))!;
        Assert.Equal("image/png", final.Tipo);
        Assert.Equal(3, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM EnviosComprovantePix").SingleAsync());
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM ComprovantesPixAtivos").SingleAsync());
        var depois = await db.Pedidos.AsNoTracking().SingleAsync();
        var reservaDepois = await db.ReservasProdutoAcabado.AsNoTracking().SingleAsync();
        Assert.Equal((antes.Situacao, antes.TotalCentavos, antes.PrazoPagamentoEmUtc),
            (depois.Situacao, depois.TotalCentavos, depois.PrazoPagamentoEmUtc));
        Assert.Equal((reservaAntes.Id, reservaAntes.Estado, reservaAntes.Quantidade),
            (reservaDepois.Id, reservaDepois.Estado, reservaDepois.Quantidade));
        Assert.Equal(movimentosAntes, await db.MovimentosProdutoAcabado.CountAsync());
        Assert.Empty((await admin.Http.GetFromJsonAsync<PaginaMovimentacoesFinanceiras>(Movimentacoes))!.Itens);
        var repetidoCheckout = await cliente.PostAsync<JsonElement>(Pedidos, carrinho, HttpStatusCode.OK, checkout);
        Assert.Equal(id, repetidoCheckout.GetProperty("id").GetGuid());

        await admin.PostAsync<JsonElement>($"/api/admin/pedidos/{id:D}/confirmar-pagamento",
            new { recebimentoConferido = true }, HttpStatusCode.OK);
        using var replay = await EnviarComprovanteAsync(cliente, rota, png, "mesmo.png", "image/png", token, chavePng, atual.Versao);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(final, await replay.Content.ReadFromJsonAsync<ComprovantePixDto>());
        using var divergente = await EnviarComprovanteAsync(cliente, rota, jpg, "divergente.jpg", "image/jpeg", token, chavePng, atual.Versao);
        Assert.Equal(HttpStatusCode.Conflict, divergente.StatusCode);
        using var pago = await EnviarComprovanteAsync(cliente, rota, png, "scan.png", "image/png", token, versao: final.Versao);
        Assert.Equal(HttpStatusCode.Conflict, pago.StatusCode);
        Assert.Equal(png, await cliente.Http.GetByteArrayAsync(rota + "/conteudo"));
        Assert.Equal(id, Assert.Single((await admin.Http.GetFromJsonAsync<PaginaMovimentacoesFinanceiras>(Movimentacoes))!.Itens).EventoId);

        var sessao = await cliente.Http.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var instante = DateTimeOffset.UtcNow.AddHours(-25);
        Pedido NovoVencido() => Pedido.Criar(sessao.GetProperty("userId").GetGuid(), instante,
            [new DadosItemPedido(produto.Id, variante.Id, produto.Nome, variante.Identificacao, 1, 1000)]);
        var prazoEncerrado = NovoVencido();
        var expirado = NovoVencido();
        expirado.Expirar(DateTimeOffset.UtcNow);
        db.Pedidos.AddRange(prazoEncerrado, expirado);
        var cancelavel = await cliente.PostAsync<JsonElement>(Pedidos, carrinho, HttpStatusCode.Created, Guid.NewGuid());
        var canceladoId = cancelavel.GetProperty("id").GetGuid();
        var rotaCancelado = $"/api/pedidos/{canceladoId:D}/comprovante-pix";
        using var anexadoAntesDeCancelar = await EnviarComprovanteAsync(cliente, rotaCancelado, png, "scan.png", "image/png", token);
        Assert.Equal(HttpStatusCode.Created, anexadoAntesDeCancelar.StatusCode);
        await admin.PostAsync<JsonElement>($"/api/admin/pedidos/{canceladoId:D}/cancelar", new { motivo = "Teste" }, HttpStatusCode.OK);
        await db.SaveChangesAsync();
        Assert.Equal(png, await cliente.Http.GetByteArrayAsync(rotaCancelado + "/conteudo"));
        foreach (var encerrado in new[] { prazoEncerrado.Id, expirado.Id, canceladoId })
        {
            using var negado = await EnviarComprovanteAsync(cliente, $"/api/pedidos/{encerrado:D}/comprovante-pix", png, "scan.png", "image/png", token);
            Assert.Equal(HttpStatusCode.Conflict, negado.StatusCode);
        }

        using var sair = await cliente.EnviarAsync("/api/auth/logout", new { });
        Assert.Equal(HttpStatusCode.NoContent, sair.StatusCode);
        using var anonimo = await cliente.Http.GetAsync(rota + "/conteudo");
        Assert.Equal(HttpStatusCode.Unauthorized, anonimo.StatusCode);
        using var postAnonimo = await EnviarComprovanteAsync(cliente, rota, png, "scan.png", "image/png", null);
        Assert.Equal(HttpStatusCode.Unauthorized, postAnonimo.StatusCode);
    }

    private static async Task<string> TokenComprovanteAsync(Sessao sessao) =>
        (await sessao.Http.GetFromJsonAsync<JsonElement>("/api/auth/csrf")).GetProperty("requestToken").GetString()!;

    private static async Task<HttpResponseMessage> EnviarComprovanteAsync(Sessao sessao, string rota,
        byte[] bytes, string nome, string mime, string? token, Guid? chave = null, Guid? versao = null, bool duplicar = false)
    {
        using var dados = new MultipartFormDataContent();
        var arquivo = new ByteArrayContent(bytes);
        arquivo.Headers.ContentType = new MediaTypeHeaderValue(mime);
        dados.Add(arquivo, "arquivo", nome);
        if (duplicar) dados.Add(new StringContent("inválido"), "outro");
        using var request = new HttpRequestMessage(HttpMethod.Post, rota) { Content = dados };
        request.Headers.Add("Idempotency-Key", (chave ?? Guid.NewGuid()).ToString("D"));
        if (versao.HasValue) request.Headers.Add("X-Comprovante-Versao", versao.Value.ToString("D"));
        if (token is not null) request.Headers.Add("X-XSRF-TOKEN", token);
        return await sessao.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    }

    internal static byte[] ImagemComprovante(SKEncodedImageFormat formato)
    {
        using var bitmap = new SKBitmap(2, 2);
        bitmap.Erase(SKColors.White);
        using var imagem = SKImage.FromBitmap(bitmap);
        using var dados = imagem.Encode(formato, 90);
        return dados.ToArray();
    }

    internal static byte[] PdfComprovante(string extra = "")
    {
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        void Objeto(string corpo)
        {
            offsets.Add(pdf.Length);
            pdf.Append(CultureInfo.InvariantCulture, $"{offsets.Count} 0 obj\n{corpo}\nendobj\n");
        }
        Objeto($"<< /Type /Catalog /Pages 2 0 R {extra} >>");
        Objeto("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Objeto("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 80 80] >>");
        var inicio = pdf.Length;
        pdf.Append("xref\n0 4\n0000000000 65535 f \n");
        foreach (var pos in offsets) pdf.Append(CultureInfo.InvariantCulture, $"{pos:D10} 00000 n \n");
        pdf.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n{inicio}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }
}
