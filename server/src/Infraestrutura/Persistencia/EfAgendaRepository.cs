using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Agenda;
using Xaquiamuni.Dominio.Agenda;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed class EfAgendaRepository(XaquiamuniDbContext db, TimeProvider relogio) : IGerenciarAgenda
{
    private IQueryable<CompromissoAgenda> Ativos => db.Set<CompromissoAgenda>().Where(x => x.ExcluidoEmUtc == null);

    public async Task<IReadOnlyList<CompromissoAgendaDto>> ListarAsync(DateOnly? inicio, DateOnly? fim, CancellationToken ct)
    {
        var consulta = Ativos.AsNoTracking();
        if (inicio.HasValue) consulta = consulta.Where(x => x.Data >= inicio.Value);
        if (fim.HasValue) consulta = consulta.Where(x => x.Data <= fim.Value);
        var compromissos = await consulta.OrderBy(x => x.Data).ThenBy(x => x.HorarioInicial).ThenBy(x => x.Id).ToListAsync(ct);
        return compromissos.Select(De).ToArray();
    }

    public async Task<CompromissoAgendaDto?> ConsultarAsync(Guid id, CancellationToken ct) =>
        await Ativos.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) is { } encontrado ? De(encontrado) : null;

    public async Task<TentativaAgendaDto?> ConsultarTentativaAsync(Guid autor, Guid chave, CancellationToken ct)
    {
        var operacao = await BuscarOperacaoAsync(autor, chave, ct);
        if (operacao is null) return null;
        var resultado = LerResultado(operacao);
        return new(operacao.Operacao, resultado.Id, resultado);
    }

    public Task<ResultadoAgenda> CriarAsync(CompromissoAgendaInput input, Guid autor, Guid chave, CancellationToken ct) =>
        GravarAsync("criar", null, input, autor, chave, () =>
        {
            if (input.Versao.HasValue) throw new ArgumentException("A criação não recebe versão.");
            var compromisso = CompromissoAgenda.Criar(input.Titulo, input.Descricao, input.Categoria,
                input.Data, input.HorarioInicial, input.HorarioFinal, autor, relogio.GetUtcNow());
            db.Add(compromisso);
            return Task.FromResult(new ResultadoAgenda(StatusAgenda.Sucesso, De(compromisso)));
        }, ct);

    public Task<ResultadoAgenda> EditarAsync(Guid id, CompromissoAgendaInput input, Guid autor, Guid chave, CancellationToken ct) =>
        GravarAsync("editar", id, input, autor, chave, () => AlterarAsync(id, input.Versao, x =>
            x.Alterar(input.Titulo, input.Descricao, input.Categoria, input.Data, input.HorarioInicial,
                input.HorarioFinal, autor, relogio.GetUtcNow()), ct), ct);

    public Task<ResultadoAgenda> ExcluirAsync(Guid id, Guid versao, Guid autor, Guid chave, CancellationToken ct) =>
        GravarAsync("excluir", id, versao, autor, chave, () =>
            AlterarAsync(id, versao, x => x.Excluir(autor, relogio.GetUtcNow()), ct), ct);

    private async Task<ResultadoAgenda> AlterarAsync(Guid id, Guid? versao, Action<CompromissoAgenda> aplicar, CancellationToken ct)
    {
        if (versao is null || versao == Guid.Empty) throw new ArgumentException("Informe a versão do compromisso.");
        var compromisso = await Ativos.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (compromisso is null) return new(StatusAgenda.Ausente);
        if (compromisso.Versao != versao) return ConflitoVersao();
        aplicar(compromisso);
        return new(StatusAgenda.Sucesso, De(compromisso));
    }

    private async Task<ResultadoAgenda> GravarAsync(string operacao, Guid? id, object input, Guid autor, Guid chave,
        Func<Task<ResultadoAgenda>> aplicar, CancellationToken ct)
    {
        if (autor == Guid.Empty || chave == Guid.Empty)
            return new(StatusAgenda.Invalida, Erro: "Informe responsável e chave de idempotência válidos.");

        var impressao = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { operacao, id, input })));
        var anterior = await BuscarOperacaoAsync(autor, chave, ct);
        if (anterior is not null) return Repetir(anterior, impressao);

        ResultadoAgenda resultado;
        try
        {
            resultado = await aplicar();
            if (resultado.Status == StatusAgenda.Sucesso)
            {
                db.Add(new OperacaoAgenda(autor, chave, operacao, impressao, JsonSerializer.Serialize(resultado.Dados)));
                await db.SaveChangesAsync(ct);
                return resultado;
            }
        }
        catch (ArgumentException erro)
        {
            return new(StatusAgenda.Invalida, Erro: erro.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            resultado = ConflitoVersao();
        }
        catch (DbUpdateException erro) when (erro.InnerException is SqlException { Number: 2601 or 2627 } sql &&
            sql.Message.Contains(AgendaMapping.ChaveOperacao, StringComparison.Ordinal))
        {
            db.ChangeTracker.Clear();
            resultado = new(StatusAgenda.Conflito, Erro: "A chave de idempotência já foi usada.");
        }

        // A gravação concorrente pode ter concluído esta chave após a primeira leitura.
        var concorrente = await BuscarOperacaoAsync(autor, chave, ct);
        return concorrente is null ? resultado : Repetir(concorrente, impressao);
    }

    private Task<OperacaoAgenda?> BuscarOperacaoAsync(Guid autor, Guid chave, CancellationToken ct) =>
        db.Set<OperacaoAgenda>().AsNoTracking().SingleOrDefaultAsync(x => x.ResponsavelId == autor && x.Chave == chave, ct);

    private static ResultadoAgenda Repetir(OperacaoAgenda operacao, string impressao) =>
        operacao.Impressao == impressao
            ? new(StatusAgenda.Repetida, LerResultado(operacao))
            : new(StatusAgenda.Conflito, Erro: "A chave de idempotência já foi usada com outros dados.");

    private static CompromissoAgendaDto LerResultado(OperacaoAgenda x) =>
        JsonSerializer.Deserialize<CompromissoAgendaDto>(x.Resultado)!;

    private static ResultadoAgenda ConflitoVersao() =>
        new(StatusAgenda.Conflito, Erro: "O compromisso foi alterado. Consulte a versão atual.");

    private static CompromissoAgendaDto De(CompromissoAgenda x) =>
        new(x.Id, x.Versao, x.Titulo, x.Descricao, x.Categoria, x.Data, x.HorarioInicial, x.HorarioFinal,
            x.CriadoPorId, x.CriadoEmUtc, x.AtualizadoPorId, x.AtualizadoEmUtc, x.ExcluidoPorId, x.ExcluidoEmUtc);
}
