using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Financeiro;
using Xaquiamuni.Dominio.Estoque;
using Xaquiamuni.Dominio.Financeiro;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed partial class EfFinanceiroRepository(XaquiamuniDbContext db, TimeProvider relogio) : IGerenciarFinanceiro
{
    private const int TamanhoPagina = 50;
    private DbSet<PagamentoFornecedor> Pagamentos => db.Set<PagamentoFornecedor>();

    public async Task<TentativaFinanceiraDto?> ConsultarTentativaAsync(Guid autor, Guid chave, CancellationToken ct)
    {
        var resultado = await db.Set<OperacaoFinanceira>().AsNoTracking()
            .Where(o => o.ResponsavelId == autor && o.Chave == chave)
            .Select(o => o.Resultado).SingleOrDefaultAsync(ct);
        if (resultado is null) return null;

        // O snapshot original conserva o tipo e o estado no momento da operação.
        using var snapshot = JsonDocument.Parse(resultado);
        if (snapshot.RootElement.TryGetProperty(nameof(PagamentoFornecedorDto.CompraId), out _))
        {
            var pagamento = snapshot.RootElement.Deserialize<PagamentoFornecedorDto>()!;
            return new("pagar", pagamento.CompraId, pagamento);
        }

        var lancamento = snapshot.RootElement.Deserialize<LancamentoDto>()!;
        return new(lancamento.CanceladoEmUtc.HasValue ? "cancelar" : "criar", lancamento.Id, lancamento);
    }

    public async Task<IReadOnlyList<LancamentoDto>> ListarAsync(int pagina, CancellationToken ct)
    {
        var lista = await db.Set<LancamentoFinanceiro>().AsNoTracking().OrderByDescending(x => x.Data)
            .ThenByDescending(x => x.Id).Skip((pagina - 1) * TamanhoPagina).Take(TamanhoPagina).ToListAsync(ct);
        return lista.Select(De).ToArray();
    }

    public async Task<LancamentoDto?> ConsultarAsync(Guid id, CancellationToken ct) =>
        await db.Set<LancamentoFinanceiro>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) is { } registro
            ? De(registro) : null;

    public async Task<CompraFinanceiraDto?> ConsultarCompraAsync(Guid id, CancellationToken ct)
    {
        var dados = await db.ComprasMateriasPrimas.AsNoTracking().Where(c => c.Id == id)
            .Select(c => new
            {
                Compra = c,
                Pago = Pagamentos.Where(p => p.CompraId == c.Id).Sum(p => p.ValorCentavos),
                TemLinhaComValor = c.Linhas.Any(l => l.ValorTotal.HasValue)
            })
            .SingleOrDefaultAsync(ct);
        if (dados is null) return null;
        var compra = dados.Compra;
        var total = Centavos(compra.Total);
        var cancelada = compra.Situacao == SituacaoCompraMateriaPrima.Cancelada;
        var saldo = cancelada ? (long?)null : checked(total - dados.Pago);
        var situacao = cancelada ? "Cancelada" : saldo is null ? "ValorAConferir" :
            saldo == 0 ? "Quitada" : dados.Pago == 0 ? "Pendente" : "Parcial";
        var composicao = total.HasValue ? "Conhecido" : dados.TemLinhaComValor ? "ParcialmenteConhecido" : "Desconhecido";
        return new(compra.Id, compra.FornecedorId, compra.NomeFornecedor, compra.DataCompra, compra.Situacao.ToString(),
            situacao, composicao, total is long conhecido ? Texto(conhecido) : null, Texto(dados.Pago),
            saldo is long devido ? Texto(devido) : null);
    }

    public async Task<IReadOnlyList<PagamentoFornecedorDto>?> ListarPagamentosAsync(Guid compra, int pagina, CancellationToken ct)
    {
        if (!await db.ComprasMateriasPrimas.AnyAsync(c => c.Id == compra, ct)) return null;
        var pagamentos = await Pagamentos.AsNoTracking().Where(p => p.CompraId == compra)
            .OrderByDescending(p => p.Data).ThenByDescending(p => p.Id)
            .Skip((pagina - 1) * TamanhoPagina).Take(TamanhoPagina).ToListAsync(ct);
        return pagamentos.Select(De).ToArray();
    }

    public Task<ResultadoFinanceiro<LancamentoDto>> CriarAsync(CriarLancamentoInput input, Guid autor, Guid chave, CancellationToken ct) =>
        ExecutarAsync<LancamentoDto>("criar", null, input, autor, chave, () =>
        {
            var registro = LancamentoFinanceiro.Criar(Interpretar<TipoLancamento>(input.Tipo), input.Categoria,
                input.Descricao, input.ValorCentavos, input.Data, Interpretar<MeioFinanceiro>(input.Meio), autor, relogio.GetUtcNow());
            db.Add(registro);
            return Task.FromResult<LancamentoDto?>(De(registro));
        }, ct);

    public Task<ResultadoFinanceiro<LancamentoDto>> CancelarAsync(Guid id, CancelarLancamentoInput input,
        Guid autor, Guid chave, CancellationToken ct) =>
        ExecutarAsync<LancamentoDto>("cancelar", id, input, autor, chave, async () =>
        {
            await LockFinanceiroSql.AdquirirAsync(db, $"finance-entry:{id:D}", ct);
            var registro = await db.Set<LancamentoFinanceiro>().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (registro is null) return null;
            registro.Cancelar(input.Motivo, autor, relogio.GetUtcNow());
            return De(registro);
        }, ct);

    public Task<ResultadoFinanceiro<PagamentoFornecedorDto>> PagarAsync(Guid compra, PagarFornecedorInput input,
        Guid autor, Guid chave, CancellationToken ct) =>
        ExecutarAsync<PagamentoFornecedorDto>("pagar", compra, input, autor, chave, async () =>
        {
            await LockFinanceiroSql.CompraAsync(db, compra, ct);
            var origem = await db.ComprasMateriasPrimas.AsNoTracking().SingleOrDefaultAsync(c => c.Id == compra, ct);
            if (origem is null) return null;
            if (origem.Situacao == SituacaoCompraMateriaPrima.Cancelada)
                throw new ConflitoFinanceiroException("Compras canceladas não aceitam pagamentos.");
            var pago = await Pagamentos.Where(p => p.CompraId == compra).SumAsync(p => p.ValorCentavos, ct);
            var pagamento = PagamentoFornecedor.Registrar(compra, input.ValorCentavos,
                checked(Centavos(origem.Total) - pago), input.Data, Interpretar<MeioFinanceiro>(input.Meio),
                input.Referencia, autor, relogio.GetUtcNow());
            Pagamentos.Add(pagamento);
            return De(pagamento);
        }, ct);

    private async Task<ResultadoFinanceiro<T>> ExecutarAsync<T>(string operacao, Guid? id, object input, Guid autor,
        Guid chave, Func<Task<T?>> executar, CancellationToken ct) where T : class
    {
        if (autor == Guid.Empty || chave == Guid.Empty)
            return new(StatusFinanceiro.Invalida, Erro: "Informe responsável e chave de idempotência válidos.");
        var impressao = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { operacao, id, input })));
        await using var transacao = await db.Database.BeginTransactionAsync(ct);
        await LockFinanceiroSql.AdquirirAsync(db, $"finance-key:{autor:D}:{chave:D}", ct);
        var anterior = await db.Set<OperacaoFinanceira>().FindAsync([autor, chave], ct);
        if (anterior is not null)
            return anterior.Impressao == impressao
                ? new(StatusFinanceiro.Repetida, JsonSerializer.Deserialize<T>(anterior.Resultado))
                : new(StatusFinanceiro.Conflito, Erro: "A chave de idempotência já foi usada com outros dados.");
        T? resultado;
        try { resultado = await executar(); }
        catch (ArgumentException erro) { return new(StatusFinanceiro.Invalida, Erro: erro.Message); }
        catch (ConflitoFinanceiroException erro) { return new(StatusFinanceiro.Conflito, Erro: erro.Message); }
        if (resultado is null) return new(StatusFinanceiro.Ausente);
        db.Add(new OperacaoFinanceira(autor, chave, impressao, JsonSerializer.Serialize(resultado)));
        await db.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
        return new(StatusFinanceiro.Sucesso, resultado);
    }

    private static T Interpretar<T>(string? texto) where T : struct, Enum =>
        Enum.TryParse<T>(texto, out var valor) && Enum.IsDefined(valor) && texto == valor.ToString()
            ? valor : throw new ArgumentException("Confira o tipo e o meio de pagamento.");

    private static long? Centavos(decimal? reais) => reais is decimal valor ? checked((long)(valor * 100m)) : null;
    private static string Texto(long valor) => valor.ToString(CultureInfo.InvariantCulture);
    private static LancamentoDto De(LancamentoFinanceiro x) =>
        new(x.Id, x.Tipo.ToString(), x.Categoria, x.Descricao, Texto(x.ValorCentavos), x.Data, x.Meio.ToString(),
            x.ResponsavelId, x.CriadoEmUtc, x.CanceladoPorId, x.CanceladoEmUtc, x.MotivoCancelamento);
    private static PagamentoFornecedorDto De(PagamentoFornecedor x) =>
        new(x.Id, x.CompraId, Texto(x.ValorCentavos), x.Data, x.Meio.ToString(), x.Referencia, x.ResponsavelId, x.CriadoEmUtc);
}
