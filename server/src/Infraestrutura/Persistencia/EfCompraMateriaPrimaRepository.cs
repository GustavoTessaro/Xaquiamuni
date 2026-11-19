using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Estoque;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed class EfCompraMateriaPrimaRepository(XaquiamuniDbContext db, TimeProvider relogio)
    : IGerenciarComprasMateriaPrima
{
    public async Task<IReadOnlyList<CompraMateriaPrima>> ListarAsync(string? busca,
        SituacaoCompraMateriaPrima? situacao, CancellationToken ct)
    {
        var query = db.ComprasMateriasPrimas.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(busca))
        {
            var termo = busca.Trim();
            query = query.Where(c => c.NomeFornecedor.Contains(termo));
        }
        if (situacao.HasValue) query = query.Where(c => c.Situacao == situacao.Value);
        return await query.OrderByDescending(c => c.DataCompra).ThenByDescending(c => c.Id).ToListAsync(ct);
    }

    public Task<CompraMateriaPrima?> ConsultarAsync(Guid id, CancellationToken ct) =>
        Detalhes().AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct);

    public Task<CompraMateriaPrima?> ConsultarCriacaoAsync(Guid autor, Guid chave, CancellationToken ct) =>
        PorChaveAsync(autor, chave, ct);

    public async Task<CompraMateriaPrimaResult> CriarAsync(CompraMateriaPrimaInput input, Guid autor,
        Guid chave, CancellationToken ct)
    {
        if (chave == Guid.Empty || input.Versao is not null)
            return new(CompraMateriaPrimaStatus.Invalida, Erro: "Informe uma chave de idempotência válida para o cadastro.");
        var impressao = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(input)));
        var existente = await PorChaveAsync(autor, chave, ct);
        if (existente is not null) return Repetir(existente, impressao);
        try
        {
            var (fornecedor, linhas) = await PrepararAsync(input, null, ct);
            var compra = CompraMateriaPrima.Criar(fornecedor, input.DataCompra, linhas, autor, chave, impressao, relogio.GetUtcNow());
            db.ComprasMateriasPrimas.Add(compra);
            await db.SaveChangesAsync(ct);
            return new(CompraMateriaPrimaStatus.Sucesso, compra);
        }
        catch (ArgumentException erro)
        {
            return new(CompraMateriaPrimaStatus.Invalida, Erro: erro.Message);
        }
        catch (DbUpdateException erro) when (erro.InnerException is SqlException { Number: 2601 or 2627 } sql &&
            sql.Message.Contains("UX_ComprasMateriasPrimas_Criacao", StringComparison.Ordinal))
        {
            db.ChangeTracker.Clear();
            var concorrente = await PorChaveAsync(autor, chave, ct);
            return concorrente is not null ? Repetir(concorrente, impressao) : Conflito();
        }
    }

    public async Task<CompraMateriaPrimaResult> EditarAsync(Guid id, CompraMateriaPrimaInput input,
        Guid autor, CancellationToken ct)
    {
        var compra = await Detalhes().SingleOrDefaultAsync(c => c.Id == id, ct);
        if (compra is null) return new(CompraMateriaPrimaStatus.Ausente);
        if (compra.Situacao != SituacaoCompraMateriaPrima.Rascunho)
            return new(CompraMateriaPrimaStatus.Conflito, Erro: "Apenas rascunhos podem ser editados.");
        if (compra.Versao != input.Versao) return Conflito();

        try
        {
            var (fornecedor, linhas) = await PrepararAsync(input, compra, ct);
            compra.Editar(fornecedor, input.DataCompra, linhas, autor, relogio.GetUtcNow());
            db.AddRange(linhas);
            return await GravarAsync(compra, ct);
        }
        catch (ArgumentException erro)
        {
            return new(CompraMateriaPrimaStatus.Invalida, Erro: erro.Message);
        }
    }

    public async Task<CompraMateriaPrimaResult> CancelarAsync(Guid id, Guid versao, Guid autor, CancellationToken ct)
    {
        var compra = await Detalhes().SingleOrDefaultAsync(c => c.Id == id, ct);
        if (compra is null) return new(CompraMateriaPrimaStatus.Ausente);
        if (compra.Situacao == SituacaoCompraMateriaPrima.Cancelada)
            return new(CompraMateriaPrimaStatus.Repetida, compra);
        if (compra.Situacao != SituacaoCompraMateriaPrima.Rascunho)
            return new(CompraMateriaPrimaStatus.Conflito, Erro: "Apenas rascunhos podem ser cancelados.");
        if (compra.Versao != versao) return Conflito();
        compra.Cancelar(autor, relogio.GetUtcNow());
        return await GravarAsync(compra, ct);
    }

    private IQueryable<CompraMateriaPrima> Detalhes() => db.ComprasMateriasPrimas
        .Include(c => c.Linhas).ThenInclude(l => l.Componentes);

    private Task<CompraMateriaPrima?> PorChaveAsync(Guid autor, Guid chave, CancellationToken ct) =>
        Detalhes().AsNoTracking().SingleOrDefaultAsync(c => c.CriadoPorId == autor && c.ChaveCriacao == chave, ct);

    private static CompraMateriaPrimaResult Repetir(CompraMateriaPrima compra, string impressao) =>
        compra.ImpressaoCriacao == impressao
            ? new(CompraMateriaPrimaStatus.Repetida, compra)
            : new(CompraMateriaPrimaStatus.Conflito, Erro: "A chave de idempotência já foi usada com outros dados.");

    private static CompraMateriaPrimaResult Conflito() =>
        new(CompraMateriaPrimaStatus.Conflito, Erro: "A compra foi alterada. Atualize os dados antes de tentar novamente.");

    private async Task<CompraMateriaPrimaResult> GravarAsync(CompraMateriaPrima compra, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return new(CompraMateriaPrimaStatus.Sucesso, compra);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return Conflito();
        }
    }

    private async Task<(Dominio.Entidades.Fornecedor Fornecedor, List<LinhaCompraMateriaPrima> Linhas)>
        PrepararAsync(CompraMateriaPrimaInput input, CompraMateriaPrima? compra, CancellationToken ct)
    {
        if (input.Linhas is not { Count: >= 1 and <= 50 } ||
            input.Linhas.Any(l => l?.Componentes is not { Count: >= 1 and <= 30 } || l.Componentes.Any(c => c is null)))
            throw new ArgumentException("Informe de 1 a 50 linhas com os materiais e quantidades.");
        var fornecedor = await db.Fornecedores.AsNoTracking().SingleOrDefaultAsync(f => f.Id == input.FornecedorId, ct);
        if (fornecedor is null || (!fornecedor.Ativo && compra?.FornecedorId != fornecedor.Id))
            throw new ArgumentException("Selecione um fornecedor ativo.");

        var insumoIds = input.Linhas.SelectMany(l => l.Componentes!).Select(c => c.InsumoId).Distinct().ToArray();
        var insumos = await db.InsumosEstoque.AsNoTracking().Include(i => i.FormasCompra)
            .Where(i => insumoIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var snapshots = compra?.Linhas.SelectMany(l => l.Componentes).ToDictionary(c => c.Id) ?? [];
        var usados = new HashSet<Guid>();
        var linhas = new List<LinhaCompraMateriaPrima>();
        foreach (var linha in input.Linhas)
        {
            var componentes = new List<ComponenteCompraMateriaPrima>();
            foreach (var componente in linha.Componentes!)
            {
                if (componente.SnapshotId is Guid snapshotId)
                {
                    if (!usados.Add(snapshotId) || !snapshots.TryGetValue(snapshotId, out var snapshot))
                        throw new ArgumentException("O material informado não pertence a este rascunho.");
                    if (snapshot.InsumoId == componente.InsumoId && snapshot.ApresentacaoId == componente.ApresentacaoId &&
                        snapshot.Quantidade == componente.Quantidade)
                    {
                        componentes.Add(snapshot.CopiarSnapshot(componentes.Count));
                        continue;
                    }
                }

                if (!insumos.TryGetValue(componente.InsumoId, out var insumo) || !insumo.Ativo)
                    throw new ArgumentException("Selecione um material ativo para cada item novo ou alterado.");
                if (componente.ApresentacaoId is Guid id && !insumo.FormasCompra.Any(f => f.Id == id && f.Ativa))
                    throw new ArgumentException("Selecione uma apresentação ativa do material.");
                componentes.Add(ComponenteCompraMateriaPrima.Criar(componentes.Count, insumo, componente.ApresentacaoId,
                    componente.Quantidade));
            }
            linhas.Add(LinhaCompraMateriaPrima.Criar(linhas.Count, linha.Kit, linha.Quantidade, linha.ValorTotal, componentes));
        }

        return (fornecedor, linhas);
    }
}
