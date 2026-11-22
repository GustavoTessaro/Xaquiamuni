using System.Data.Common;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SkiaSharp;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Aplicacao.Imagens;
using Xaquiamuni.Aplicacao.Outbox;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Dominio.Entidades;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Infraestrutura.Autenticacao;
using Xaquiamuni.Infraestrutura.Outbox;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests.Integration;

internal sealed class JornadaCompraE2E(HostE2ECompra host, RelogioJornadaCompra relogio, Guid admin, string email)
{
    private Guid _produto;
    private Guid _variante;
    private Guid[] _lotes = [];
    private Guid _pedidoPrincipal;
    private Guid _pedidoInelegivel;
    private DateTimeOffset? _pagamento;
    private DateTimeOffset? _preparacao;
    private DateTimeOffset? _retirada;
    private DateTimeOffset? _entrega;

    internal async Task<object> SemearAsync(string nomeProduto = "Sabonete E2E", string nomeCategoria = "Artesanais E2E",
        bool comVariante = false)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<XaquiamuniDbContext>();
        var categoria = Categoria.Criar(nomeCategoria, null, 0);
        var produto = comVariante
            ? Produto.Criar(nomeProduto, null, categoria,
                [ProdutoVariante.Criar("Lavanda", "UN", 13.90m, padrao: true), ProdutoVariante.Criar("Alecrim", "UN", 20m)])
            : Produto.CriarSimples(nomeProduto, null, categoria, "UN", 13.90m);
        var variante = produto.Variantes.Single(v => v.Padrao);
        using var bitmap = new SKBitmap(64, 64);
        bitmap.Erase(SKColors.ForestGreen);
        using var imagem = SKImage.FromBitmap(bitmap);
        using var bytes = imagem.Encode(SKEncodedImageFormat.Webp, 85);
        var chave = ChaveArmazenamentoImagemProduto.Gerar();
        await using var conteudo = bytes.AsStream();
        await sp.GetRequiredService<IArmazenamentoImagemProduto>().ArmazenarAsync(chave, conteudo);
        produto.AdicionarImagem(chave, "e2e.webp", "image/webp", bytes.Size, 64, 64);
        produto.Publicar();
        db.Produtos.Add(produto);
        await db.SaveChangesAsync();

        var estoque = sp.GetRequiredService<IEstoqueProdutoAcabadoRepository>();
        var hoje = sp.GetRequiredService<ICalendarioOperacionalEstoque>().ObterDataAtual();
        var lotes = new List<Guid>();
        foreach (var (quantidade, dias) in new[] { (1, 5), (2, 7), (4, 10) })
        {
            var entrada = await estoque.RegistrarEntradaAsync(new(variante.Id, admin, TipoMovimentoProdutoAcabado.EntradaManual,
                quantidade, CondicaoEstoqueProdutoAcabado.Vendavel, "Seed E2E", Guid.NewGuid().ToString("N"),
                DataFabricacaoNovoLote: hoje, DataValidadeNovoLote: hoje.AddDays(dias)));
            Assert.True(entrada.Sucesso);
            lotes.Add(entrada.Lote!.Id);
        }
        _produto = produto.Id;
        _variante = variante.Id;
        _lotes = lotes.ToArray();
        var alternativa = produto.Variantes.SingleOrDefault(v => !v.Padrao);
        if (alternativa is not null)
        {
            var entrada = await estoque.RegistrarEntradaAsync(new(alternativa.Id, admin, TipoMovimentoProdutoAcabado.EntradaManual,
                3, CondicaoEstoqueProdutoAcabado.Vendavel, "Seed E2E", Guid.NewGuid().ToString("N"),
                DataFabricacaoNovoLote: hoje, DataValidadeNovoLote: hoje.AddDays(5)));
            Assert.True(entrada.Sucesso);
        }
        return new { produto = _produto, variante = _variante, alternativa = alternativa?.Id };
    }

    internal async Task<string> CapturarConfirmacaoAsync(CancellationToken token)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        sp.GetRequiredService<IOptions<EmailConfirmationEmailOptions>>().Value.PublicFrontendBaseUrl = host.Origin;
        var outbox = sp.GetRequiredService<IEmailOutboxStore>();
        var claim = Assert.Single(await outbox.ClaimDueAsync(
            EmailOutboxMessageType.EmailConfirmation, 1, TimeSpan.FromMinutes(1), token));
        await sp.GetRequiredService<EmailConfirmationOutboxProcessor>().ProcessAsync(claim, token);
        var mensagem = Assert.Single(sp.GetRequiredService<CaixaEmailE2E>().Mensagens);
        Assert.True(mensagem.Recipient == email, "Destinatário sintético inesperado.");
        var link = WebUtility.HtmlDecode(Regex.Match(mensagem.Content, "href=\"([^\"]+)\"").Groups[1].Value);
        Assert.True(Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.GetLeftPart(UriPartial.Authority) == host.Origin
            && uri.AbsolutePath == "/confirm-email" && uri.Fragment.Length > 1, "Link de confirmação local inválido.");
        return uri!.PathAndQuery + uri.Fragment;
    }

    internal async Task ConferirAsync(string etapa, Guid id, CancellationToken token)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<XaquiamuniDbContext>();
        var pedido = await db.Pedidos.AsNoTracking().Include(p => p.Itens).SingleAsync(p => p.Id == id, token);
        var reservas = await db.ReservasProdutoAcabado.AsNoTracking().Where(r => r.PedidoId == id).ToListAsync(token);
        var ids = reservas.Select(r => (Guid?)r.Id).ToArray();
        var movimentos = await db.MovimentosProdutoAcabado.AsNoTracking()
            .Where(m => ids.Contains(m.ReservaProdutoAcabadoId)).ToListAsync(token);
        var saldos = await db.SaldosProdutoAcabado.AsNoTracking().Where(s => _lotes.Contains(s.LoteId)).ToListAsync(token);
        Assert.Equal(TimeSpan.Zero, pedido.CriadoEmUtc.Offset);
        Assert.Equal(pedido.CriadoEmUtc.AddHours(24), pedido.PrazoPagamentoEmUtc);
        Assert.All(reservas, r => Assert.Equal(pedido.PrazoPagamentoEmUtc, r.ExpiraEmUtc));
        var item = Assert.Single(pedido.Itens);
        Assert.Equal((_produto, _variante, 1390L), (item.ProdutoId, item.ProdutoVarianteId, item.PrecoUnitarioCentavos));

        if (etapa == "avancar-relogio-lote")
        {
            _pedidoInelegivel = id;
            Assert.Equal(SituacaoPedido.ProntoParaRetirada, pedido.Situacao);
            Assert.Equal(2, item.Quantidade);
            Assert.Equal(_lotes.Skip(1).Order(), reservas.Select(r => r.LoteId).Order());
            relogio.Definir(pedido.CriadoEmUtc.AddDays(8));
            return;
        }
        if (etapa == "lote-inelegivel")
        {
            relogio.Definir(null);
            Assert.Equal(_pedidoInelegivel, id);
            Assert.Equal(SituacaoPedido.ProntoParaRetirada, pedido.Situacao);
            Assert.Equal(2, reservas.Count);
            Assert.All(reservas, reserva => Assert.Equal(EstadoReservaProdutoAcabado.Confirmada, reserva.Estado));
            Assert.Equal(1, saldos.Single(s => s.LoteId == _lotes[1]).QuantidadeFisica);
            Assert.Equal(4, saldos.Single(s => s.LoteId == _lotes[2]).QuantidadeFisica);
            Assert.Empty(movimentos);
            Assert.Equal(5, saldos.Sum(s => s.QuantidadeFisica));
            return;
        }
        if (etapa == "expirar")
        {
            Assert.Equal(SituacaoPedido.AguardandoPagamento, pedido.Situacao);
            relogio.Definir(pedido.PrazoPagamentoEmUtc);
            try
            {
                Assert.Equal(EncerrarPedidoStatus.Expirado, await sp.GetRequiredService<EncerrarPedidoCompra>().ExpirarAsync(id, token));
                Assert.Equal(SituacaoPedido.Expirado, await db.Pedidos.Where(p => p.Id == id).Select(p => p.Situacao).SingleAsync(token));
                Assert.Equal(EstadoReservaProdutoAcabado.Expirada, await db.ReservasProdutoAcabado.Where(r => r.PedidoId == id)
                    .Select(r => r.Estado).SingleAsync(token));
                Assert.Equal(5, saldos.Sum(s => s.QuantidadeFisica));
                Assert.Empty(movimentos);
            }
            finally { relogio.Definir(null); }
            return;
        }

        if (etapa == "comprovante-recuperado")
        {
            var envios = await db.Database.SqlQuery<int>(
                $"SELECT COUNT(*) AS [Value] FROM EnviosComprovantePix WHERE PedidoId = {id}").SingleAsync(token);
            var ativos = await db.Database.SqlQuery<int>(
                $"SELECT COUNT(*) AS [Value] FROM ComprovantesPixAtivos WHERE PedidoId = {id}").SingleAsync(token);
            var cadeia = await db.Database.SqlQuery<int>($"""
                SELECT COUNT(*) AS [Value]
                FROM EnviosComprovantePix anterior
                JOIN EnviosComprovantePix atual ON atual.VersaoAnterior = anterior.Id
                JOIN ComprovantesPixAtivos ativo ON ativo.EnvioId = atual.Id AND ativo.PedidoId = atual.PedidoId
                WHERE anterior.PedidoId = {id} AND atual.PedidoId = {id} AND anterior.Sha256 <> atual.Sha256
                """).SingleAsync(token);
            Assert.Equal((2, 1, 1), (envios, ativos, cadeia));
            Assert.Equal(SituacaoPedido.AguardandoPagamento, pedido.Situacao);
            Assert.Null(pedido.PagamentoConfirmadoEmUtc);
            Assert.Empty(movimentos);
            Assert.All(reservas, reserva => Assert.Equal(EstadoReservaProdutoAcabado.Ativa, reserva.Estado));
            Assert.Equal(7, saldos.Sum(saldo => saldo.QuantidadeFisica));
            return;
        }

        if (etapa == "pedido") _pedidoPrincipal = id;
        Assert.Equal(_pedidoPrincipal, id);
        Assert.Equal(1, await db.Pedidos.CountAsync(token));
        Assert.Equal(2, await db.ReservasProdutoAcabado.CountAsync(token));
        Assert.Equal(2, item.Quantidade);
        Assert.Equal(2780, pedido.TotalCentavos);
        Assert.Equal(_lotes.Take(2).Order(), reservas.Select(r => r.LoteId).Order());
        Assert.All(reservas, r => Assert.Equal(1, r.Quantidade));
        var situacao = etapa switch
        {
            "pedido" => SituacaoPedido.AguardandoPagamento,
            "pagamento" or "pagamento-repetido" => SituacaoPedido.PagamentoConfirmado,
            "preparacao" => SituacaoPedido.EmPreparacao,
            "retirada" => SituacaoPedido.ProntoParaRetirada,
            "entrega" or "entrega-repetida" => SituacaoPedido.Entregue,
            _ => throw new InvalidOperationException("Checkpoint E2E inesperado."),
        };
        Assert.Equal(situacao, pedido.Situacao);
        if (etapa == "pagamento") _pagamento = pedido.PagamentoConfirmadoEmUtc;
        if (etapa == "preparacao") _preparacao = pedido.PreparacaoIniciadaEmUtc;
        if (etapa == "retirada") _retirada = pedido.RetiradaAutorizadaEmUtc;
        if (etapa == "entrega") _entrega = pedido.EncerradoEmUtc;
        if (situacao >= SituacaoPedido.PagamentoConfirmado)
        {
            Assert.Equal(admin, pedido.PagamentoConfirmadoPorId);
            Assert.Equal(_pagamento, pedido.PagamentoConfirmadoEmUtc);
            Assert.Equal(TimeSpan.Zero, pedido.PagamentoConfirmadoEmUtc!.Value.Offset);
        }
        if (situacao >= SituacaoPedido.EmPreparacao)
        {
            Assert.Equal(admin, pedido.PreparacaoIniciadaPorId);
            Assert.Equal(_preparacao, pedido.PreparacaoIniciadaEmUtc);
            Assert.Equal(TimeSpan.Zero, pedido.PreparacaoIniciadaEmUtc!.Value.Offset);
            Assert.True(pedido.PreparacaoIniciadaEmUtc >= _pagamento);
        }
        if (situacao >= SituacaoPedido.ProntoParaRetirada)
        {
            Assert.Equal(admin, pedido.RetiradaAutorizadaPorId);
            Assert.Equal(_retirada, pedido.RetiradaAutorizadaEmUtc);
            Assert.Equal(TimeSpan.Zero, pedido.RetiradaAutorizadaEmUtc!.Value.Offset);
            Assert.True(pedido.RetiradaAutorizadaEmUtc >= pedido.PreparacaoIniciadaEmUtc);
        }

        if (situacao == SituacaoPedido.Entregue)
        {
            Assert.Equal(admin, pedido.EncerradoPorId);
            Assert.Equal(_entrega, pedido.EncerradoEmUtc);
            Assert.Equal(TimeSpan.Zero, pedido.EncerradoEmUtc!.Value.Offset);
            Assert.True(ConfirmarEntregaPedido.MovimentosCorrespondem(pedido, reservas, movimentos));
            Assert.All(reservas, r => Assert.Equal(EstadoReservaProdutoAcabado.Consumida, r.Estado));
            Assert.Equal(0, saldos.Single(s => s.LoteId == _lotes[0]).QuantidadeFisica);
            Assert.Equal(1, saldos.Single(s => s.LoteId == _lotes[1]).QuantidadeFisica);
            Assert.Equal(4, saldos.Single(s => s.LoteId == _lotes[2]).QuantidadeFisica);
            Assert.Equal(5, await db.MovimentosProdutoAcabado.CountAsync(m => _lotes.Contains(m.LoteId), token));
            return;
        }

        Assert.Empty(movimentos);
        Assert.Equal(3, await db.MovimentosProdutoAcabado.CountAsync(m => _lotes.Contains(m.LoteId), token));
        Assert.Equal(7, saldos.Sum(s => s.QuantidadeFisica));
        Assert.All(reservas, r => Assert.Equal(situacao == SituacaoPedido.AguardandoPagamento
            ? EstadoReservaProdutoAcabado.Ativa : EstadoReservaProdutoAcabado.Confirmada, r.Estado));
    }
}

internal sealed class RelogioJornadaCompra : DbCommandInterceptor
{
    private long _ticks;
    internal void Definir(DateTimeOffset? instante) => Interlocked.Exchange(ref _ticks, instante?.UtcTicks ?? 0);

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        const string sql = "TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00')";
        var ticks = Interlocked.Read(ref _ticks);
        if (ticks != 0 && command.CommandText.Contains(sql, StringComparison.Ordinal))
        {
            var parametro = command.CreateParameter();
            parametro.ParameterName = "@utcJornadaE2E";
            parametro.DbType = System.Data.DbType.DateTimeOffset;
            parametro.Scale = 7;
            parametro.Value = new DateTimeOffset(ticks, TimeSpan.Zero);
            command.Parameters.Add(parametro);
            command.CommandText = command.CommandText.Replace(sql, parametro.ParameterName, StringComparison.Ordinal);
        }
        return ValueTask.FromResult(result);
    }
}
