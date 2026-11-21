using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Catalogo;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed record ConfigurarPromocao(Guid ProdutoId, string? Tipo, string? Escopo, string? Valor,
    DateTimeOffset? InicioUtc, DateTimeOffset? FimUtc, Guid[]? VariantesIds);

public sealed record DetalhePromocao(Guid Id, Guid ProdutoId, string Tipo, string Escopo, string Valor,
    DateTimeOffset? InicioUtc, DateTimeOffset? FimUtc, bool Ativa, Guid[] VariantesIds, int Versao,
    Guid CriadaPorId, DateTimeOffset CriadaEmUtc, Guid AlteradaPorId, DateTimeOffset AlteradaEmUtc);

public sealed record PrecoPromocaoAdmin(string RegularCentavos, string FinalCentavos, string EconomiaCentavos,
    int OffPontosBase);

public sealed record PreviaVariante(Guid VarianteId, string Identificacao, bool Ativa, bool Abrangida,
    string RegularCentavos, PrecoPromocaoAdmin? PrecoProposto, PrecoPromocaoAdmin PrecoEfetivo,
    bool PrecoIncompativel, Guid? PromocaoEfetivaId, bool PropostaEfetiva, bool EspecificaPrevalece);

public sealed record PreviaPromocao(DateTimeOffset InstanteUtc, PreviaVariante[] Variantes, Guid[] Conflitos);

public sealed record ResultadoPromocao(int Status, string? Codigo = null, DetalhePromocao? Detalhe = null,
    PreviaPromocao? Previa = null);

public sealed class EfPromocoes(XaquiamuniDbContext db, TimeProvider relogio)
{
    public async Task<DetalhePromocao[]> ListarAsync(Guid? produtoId, int pagina, CancellationToken ct)
    {
        var promocoes = await Consulta().AsNoTracking()
            .Where(p => produtoId == null || p.ProdutoId == produtoId)
            .OrderByDescending(p => p.CriadaEmUtc).ThenBy(p => p.Id)
            .Skip((pagina - 1) * 50).Take(50).ToListAsync(ct);
        return promocoes.Select(Mapear).ToArray();
    }

    public async Task<DetalhePromocao?> ConsultarAsync(Guid id, CancellationToken ct)
    {
        var promocao = await Consulta().AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
        return promocao is null ? null : Mapear(promocao);
    }

    public async Task<ResultadoPromocao> PreverAsync(ConfigurarPromocao dados, Guid? substituiId,
        DateTimeOffset? instante, CancellationToken ct)
    {
        var proposta = Promocao.Criar(dados.ProdutoId, Guid.Empty, relogio.GetUtcNow());
        if (!Configurar(proposta, dados)) return new(400, "configuracao_invalida");
        if (substituiId is not null && !await db.Set<Promocao>()
            .AnyAsync(p => p.Id == substituiId && p.ProdutoId == proposta.ProdutoId, ct))
            return new(404, "promocao_nao_encontrada");

        var variantes = await VariantesAsync(proposta.ProdutoId, ct);
        var erro = await ValidarProdutoAsync(proposta, variantes, ct);
        if (erro is not null) return erro;
        proposta.RegistrarAlteracao(true, Guid.Empty, relogio.GetUtcNow());
        var promocoes = await AtivasAsync(proposta, substituiId, ct);
        var conflitos = promocoes.Where(p => Conflitam(proposta, p)).Select(p => p.Id).Order().ToArray();
        if (conflitos.Length == 0) promocoes.Add(proposta);
        var data = instante?.ToUniversalTime() ?? relogio.GetUtcNow();
        return new(200, Previa: new(data, variantes.Select(v => PreverVariante(proposta, promocoes, v, data)).ToArray(), conflitos));
    }

    public async Task<ResultadoPromocao> SalvarAsync(Guid? id, ConfigurarPromocao? dados, int? versao,
        bool? ativa, Guid autor, Guid chave, CancellationToken ct)
    {
        var impressao = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            id, dados = dados is null ? null : dados with { VariantesIds = dados.VariantesIds?.Order().ToArray() },
            versao, ativa
        })));
        try
        {
            await using var transacao = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            // Todos os comandos ficam serializados antes de ler promoções, preços ou idempotência.
            await PromocoesComerciaisSql.BloquearAsync(db, escrita: true, ct);
            var operacao = await db.Set<OperacaoPromocao>().FindAsync([autor, chave], ct);
            if (operacao is not null)
                return operacao.Impressao == impressao
                    ? JsonSerializer.Deserialize<ResultadoPromocao>(operacao.Resultado)!
                    : new(409, "chave_em_uso");

            var agora = relogio.GetUtcNow();
            var promocao = id is null
                ? Promocao.Criar(dados!.ProdutoId, autor, agora)
                : await Consulta().SingleOrDefaultAsync(p => p.Id == id, ct);
            if (promocao is null) return new(404, "promocao_nao_encontrada");
            if (id is not null && promocao.Versao != versao) return new(409, "versao_divergente");
            if (dados is not null && !Configurar(promocao, dados)) return new(400, "configuracao_invalida");
            var habilitada = ativa ?? promocao.Ativa;
            if (dados is not null || habilitada)
            {
                var variantes = await VariantesAsync(promocao.ProdutoId, ct);
                var erro = await ValidarProdutoAsync(promocao, variantes, ct);
                if (erro is not null) return erro;
                if (habilitada)
                {
                    var abrangidas = variantes.Where(v => v.Ativa && promocao.Abrange(v.Id)).ToArray();
                    if (abrangidas.Length == 0 || abrangidas.Any(v => Calcular(promocao, v) is null))
                        return new(400, "preco_promocional_invalido");
                    var existentes = await AtivasAsync(promocao, id, ct);
                    if (existentes.Any(p => Conflitam(promocao, p))) return new(409, "vigencia_sobreposta");
                }
            }

            promocao.RegistrarAlteracao(habilitada, autor, agora);
            if (id is null) db.Add(promocao);
            var resultado = new ResultadoPromocao(id is null ? 201 : 200, Detalhe: Mapear(promocao));
            db.Add(new OperacaoPromocao(autor, chave, impressao, JsonSerializer.Serialize(resultado)));
            await db.SaveChangesAsync(ct);
            await transacao.CommitAsync(ct);
            return resultado;
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(409, "versao_divergente");
        }
        catch (SqlException e) when (e.Number is 1205 or 1222 or 51000)
        {
            return new(409, "conflito_concorrencia");
        }
        catch (DbUpdateException e) when (e.InnerException is SqlException { Number: 1205 or 1222 })
        {
            return new(409, "conflito_concorrencia");
        }
    }

    private IQueryable<Promocao> Consulta() => db.Set<Promocao>().Include(p => p.Variantes);

    private Task<List<Promocao>> AtivasAsync(Promocao promocao, Guid? ignora, CancellationToken ct) =>
        Consulta().AsNoTracking().Where(p => p.ProdutoId == promocao.ProdutoId && p.Ativa && p.Id != ignora).ToListAsync(ct);

    private Task<List<ProdutoVariante>> VariantesAsync(Guid produtoId, CancellationToken ct) =>
        db.ProdutoVariantes.AsNoTracking().Where(v => v.ProdutoId == produtoId)
            .OrderBy(v => v.Identificacao).ThenBy(v => v.Id).ToListAsync(ct);

    private async Task<ResultadoPromocao?> ValidarProdutoAsync(Promocao promocao,
        List<ProdutoVariante> variantes, CancellationToken ct)
    {
        if (!await db.Produtos.AnyAsync(p => p.Id == promocao.ProdutoId, ct))
            return new(404, "produto_nao_encontrado");
        return promocao.Variantes.Any(p => !variantes.Any(v => v.Id == p.VarianteId))
            ? new(400, "variantes_invalidas") : null;
    }

    private static bool Configurar(Promocao promocao, ConfigurarPromocao dados)
    {
        if (promocao.ProdutoId != dados.ProdutoId || dados.ProdutoId == Guid.Empty ||
            !Enum.TryParse<TipoPromocao>(dados.Tipo, out var tipo) || tipo.ToString() != dados.Tipo ||
            !Enum.TryParse<EscopoPromocao>(dados.Escopo, out var escopo) || escopo.ToString() != dados.Escopo ||
            !long.TryParse(dados.Valor, NumberStyles.None, CultureInfo.InvariantCulture, out var valor) ||
            dados.VariantesIds?.Length > 500)
            return false;
        try
        {
            promocao.Configurar(tipo, escopo, valor, dados.InicioUtc, dados.FimUtc, dados.VariantesIds ?? []);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool Conflitam(Promocao a, Promocao b) =>
        a.Escopo == b.Escopo && a.Sobrepoe(b) &&
        (a.Escopo == EscopoPromocao.TodasVariantes || a.Variantes.Any(v => b.Abrange(v.VarianteId)));

    private static PreviaVariante PreverVariante(Promocao proposta, List<Promocao> todas, ProdutoVariante variante,
        DateTimeOffset instante)
    {
        var regular = Regular(variante);
        var abrangida = proposta.Abrange(variante.Id);
        var sugerido = abrangida ? Calcular(proposta, variante) : null;
        var (efetiva, preco, precedencia) = variante.Ativa
            ? PrecosPromocionais.Resolver(todas, variante.Id, regular, instante) : default;
        var usaProposta = efetiva?.Id == proposta.Id;
        return new(variante.Id, variante.Identificacao, variante.Ativa, abrangida, Texto(regular),
            sugerido is null ? null : Mapear(sugerido), Mapear(preco ?? new(regular, regular, 0)),
            abrangida && sugerido is null, usaProposta ? null : efetiva?.Id, usaProposta, precedencia);
    }

    private static long Regular(ProdutoVariante variante) => checked((long)(variante.PrecoVenda * 100m));
    private static PrecoPromocional? Calcular(Promocao promocao, ProdutoVariante variante) =>
        PrecosPromocionais.Calcular(Regular(variante), promocao.Tipo, promocao.Valor);
    private static PrecoPromocaoAdmin Mapear(PrecoPromocional preco) =>
        new(Texto(preco.Regular), Texto(preco.Final), Texto(preco.Economia), preco.OffPontosBase);
    private static string Texto(long valor) => valor.ToString(CultureInfo.InvariantCulture);
    private static DetalhePromocao Mapear(Promocao p) =>
        new(p.Id, p.ProdutoId, p.Tipo.ToString(), p.Escopo.ToString(), Texto(p.Valor), p.InicioUtc, p.FimUtc,
            p.Ativa, p.Variantes.Select(v => v.VarianteId).Order().ToArray(), p.Versao,
            p.CriadaPorId, p.CriadaEmUtc, p.AlteradaPorId, p.AlteradaEmUtc);
}
