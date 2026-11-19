using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Catalogo;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed class EfFornecedorRepository(XaquiamuniDbContext db, TimeProvider relogio) : IGerenciarFornecedores
{
    public async Task<IReadOnlyList<Fornecedor>> ListarAsync(string? busca, bool? ativo, CancellationToken ct)
    {
        var query = db.Fornecedores.AsNoTracking();
        var termo = Fornecedor.NormalizarNome(busca).ToUpperInvariant();
        if (termo.Length > 0) query = query.Where(f => f.NomeNormalizado.Contains(termo));
        if (ativo is bool filtro) query = query.Where(f => f.Ativo == filtro);
        return await query.OrderBy(f => f.NomeNormalizado).ThenBy(f => f.Id).ToListAsync(ct);
    }

    public Task<Fornecedor?> ConsultarAsync(Guid id, CancellationToken ct) =>
        db.Fornecedores.AsNoTracking().SingleOrDefaultAsync(f => f.Id == id, ct);

    public Task<ResultadoFornecedor> CadastrarAsync(DadosFornecedor dados, CancellationToken ct) =>
        GravarAsync(() =>
        {
            var fornecedor = Fornecedor.Criar(dados.Nome, dados.Telefone, dados.Email, dados.Observacoes, relogio.GetUtcNow());
            db.Fornecedores.Add(fornecedor);
            return fornecedor;
        }, ct);

    public async Task<ResultadoFornecedor> EditarAsync(Guid id, DadosFornecedor dados, CancellationToken ct)
    {
        var fornecedor = await db.Fornecedores.FindAsync([id], ct);
        if (fornecedor is null) return new(FornecedorStatus.NaoEncontrado);
        return await GravarAsync(() =>
        {
            fornecedor.Editar(dados.Nome, dados.Telefone, dados.Email, dados.Observacoes, relogio.GetUtcNow());
            return fornecedor;
        }, ct);
    }

    public async Task<ResultadoFornecedor> AlterarStatusAsync(Guid id, bool ativo, CancellationToken ct)
    {
        var fornecedor = await db.Fornecedores.FindAsync([id], ct);
        if (fornecedor is null) return new(FornecedorStatus.NaoEncontrado);
        fornecedor.DefinirAtivo(ativo, relogio.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return new(FornecedorStatus.Sucesso, fornecedor);
    }

    private async Task<ResultadoFornecedor> GravarAsync(Func<Fornecedor> aplicar, CancellationToken ct)
    {
        try
        {
            var fornecedor = aplicar();
            await db.SaveChangesAsync(ct);
            return new(FornecedorStatus.Sucesso, fornecedor);
        }
        catch (ArgumentException erro)
        {
            return new(FornecedorStatus.Invalido, Erro: erro.Message);
        }
        catch (DbUpdateException erro) when (erro.InnerException is SqlException { Number: 2601 or 2627 } sql &&
            sql.Message.Contains("UX_Fornecedores_NomeNormalizado", StringComparison.Ordinal))
        {
            foreach (var entrada in erro.Entries) entrada.State = EntityState.Detached;
            return new(FornecedorStatus.NomeDuplicado, Erro: "Já existe um fornecedor com esse nome.");
        }
    }
}
