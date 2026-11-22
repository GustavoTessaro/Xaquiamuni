using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Pedidos;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Infraestrutura.Persistencia;

internal sealed record OperacaoAtendimento(Guid Autor, Guid Chave, string Impressao,
    string Operacao, Guid? CidadeId, DateTimeOffset RegistradaEmUtc, string ResultadoJson);

public sealed partial class EfConfiguracoesAtendimento(XaquiamuniDbContext db, TimeProvider relogio) : IConfiguracoesAtendimento
{
    internal const string RecursoLock = "store-fulfillment-config";
    private static readonly string[] Ufs =
        ["11RO", "12AC", "13AM", "14RR", "15PA", "16AP", "17TO", "21MA", "22PI", "23CE", "24RN",
         "25PB", "26PE", "27AL", "28SE", "29BA", "31MG", "32ES", "33RJ", "35SP", "41PR", "42SC",
         "43RS", "50MS", "51MT", "52GO", "53DF"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private DbSet<ConfiguracaoAtendimento> Configuracoes => db.Set<ConfiguracaoAtendimento>();
    private DbSet<CidadeEntregaPropria> Cidades => db.Set<CidadeEntregaPropria>();

    public async Task<AtendimentoAdmin> ConsultarAsync(CancellationToken ct) =>
        Mapear(await Configuracoes.AsNoTracking().SingleOrDefaultAsync(ct) ?? new());

    public async Task<ModalidadesPublicas> ModalidadesAsync(CancellationToken ct) =>
        await Configuracoes.AsNoTracking().Select(c => new ModalidadesPublicas(c.RetiradaHabilitada,
            c.EntregaPropriaHabilitada && Cidades.Any(x => x.Ativa), false)).SingleOrDefaultAsync(ct)
        ?? new(true, false, false);

    public Task<string?> ContatoAsync(CancellationToken ct) =>
        Configuracoes.AsNoTracking().Where(c => c.WhatsAppConfirmado && c.WhatsAppHabilitado)
            .Select(c => c.WhatsApp).SingleOrDefaultAsync(ct);

    public async Task<IReadOnlyList<CidadeEntregaDto>> CidadesAsync(CancellationToken ct) =>
        (await Cidades.AsNoTracking().OrderBy(c => c.Uf).ThenBy(c => c.Nome)
            .ThenBy(c => c.CodigoIbge).ToListAsync(ct)).Select(CidadeEntregaDto.De).ToArray();

    public async Task<CidadeEntregaDto?> CidadeAsync(Guid id, CancellationToken ct) =>
        await Cidades.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct) is { } cidade
            ? CidadeEntregaDto.De(cidade) : null;

    public Task<ResultadoAtendimento> AtualizarModalidadesAsync(SalvarModalidades input, Guid autor, Guid chave, CancellationToken ct) =>
        ExecutarAsync("modalidades", null, input, autor, chave, async agora =>
        {
            if (!RevisaoValida(input.Revisao) || input.RetiradaHabilitada is not bool retirada ||
                input.EntregaPropriaHabilitada is not bool propria)
                return Invalido("Informe as modalidades e a revisão atual.");
            if (input.CorreiosHabilitados == true)
                return Invalido("Correios indisponíveis enquanto a integração não estiver homologada.");
            var endereco = Texto(input.EnderecoRetirada);
            var instrucoes = Texto(input.InstrucoesRetirada);
            if (endereco?.Length > 300 || instrucoes?.Length > 600)
                return Invalido("Use até 300 caracteres no endereço e 600 nas instruções.");

            var config = await CarregarConfiguracaoAsync(ct);
            if (config.Revisao != input.Revisao) return RevisaoDivergente();
            if (!retirada && (!propria || !await Cidades.AnyAsync(c => c.Ativa, ct))) return UltimaModalidade();
            config.RetiradaHabilitada = retirada;
            config.EntregaPropriaHabilitada = propria;
            config.EnderecoRetirada = endereco;
            config.InstrucoesRetirada = instrucoes;
            AtualizarAuditoria(config, autor, agora);
            return Sucesso(Mapear(config));
        }, ct);

    public Task<ResultadoAtendimento> AtualizarWhatsAppAsync(SalvarWhatsApp input, Guid autor, Guid chave, CancellationToken ct) =>
        ExecutarAsync("whatsapp", null, input, autor, chave, async agora =>
        {
            if (!RevisaoValida(input.Revisao) || input.Confirmado is not bool confirmado || input.Habilitado is not bool habilitado)
                return Invalido("Informe confirmação, habilitação e revisão.");
            var informado = Texto(input.Numero);
            var numero = informado is null ? null : NormalizarTelefone(informado);
            if (informado is not null && numero is null)
                return Invalido("Informe um número internacional válido com o prefixo +.");
            if ((confirmado && numero is null) || (habilitado && !confirmado))
                return Invalido("Confirme o número antes de habilitar o contato público.");

            var config = await CarregarConfiguracaoAsync(ct);
            if (config.Revisao != input.Revisao) return RevisaoDivergente();
            config.WhatsApp = numero;
            config.WhatsAppConfirmado = confirmado;
            config.WhatsAppHabilitado = habilitado;
            AtualizarAuditoria(config, autor, agora);
            return Sucesso(Mapear(config));
        }, ct);

    public Task<ResultadoAtendimento> SalvarCidadeAsync(Guid? id, SalvarCidade input, Guid autor, Guid chave, CancellationToken ct) =>
        ExecutarAsync(id is null ? "criar-cidade" : "editar-cidade", id, input, autor, chave, async agora =>
        {
            var codigo = input.CodigoIbge?.Trim();
            var nome = Texto(input.Nome);
            var uf = input.Uf?.Trim().ToUpperInvariant();
            if (codigo?.Length != 7 || !codigo.All(char.IsAsciiDigit) ||
                !Ufs.Contains(codigo[..2] + uf) || nome is null || nome.Length > 100 ||
                input.TarifaCentavos is not long tarifa || tarifa < 0 || input.Ativa is not bool ativa ||
                (id.HasValue ? !RevisaoValida(input.Revisao) : input.Revisao.HasValue))
                return Invalido("Informe código IBGE compatível com a UF, nome, tarifa não negativa e situação. Edições exigem revisão.");

            CidadeEntregaPropria cidade;
            if (id is { } existente)
            {
                var atual = await Cidades.SingleOrDefaultAsync(c => c.Id == existente, ct);
                if (atual is null) return Ausente();
                if (atual.Revisao != input.Revisao) return RevisaoDivergente();
                if (atual.Ativa && !ativa && !await PermiteRemoverCidadeAtivaAsync(existente, ct))
                    return UltimaModalidade();
                cidade = atual;
            }
            else
            {
                cidade = new() { CriadoPor = autor, CriadoEmUtc = agora };
                Cidades.Add(cidade);
            }

            if (await Cidades.AnyAsync(c => c.CodigoIbge == codigo && c.Id != cidade.Id, ct))
                return Conflito("Já existe uma cidade com este código IBGE.");
            cidade.CodigoIbge = codigo;
            cidade.Nome = nome;
            cidade.Uf = uf!;
            cidade.TarifaCentavos = tarifa;
            cidade.Ativa = ativa;
            cidade.Revisao++;
            cidade.AlteradoPor = autor;
            cidade.AlteradoEmUtc = agora;
            return Sucesso(CidadeEntregaDto.De(cidade));
        }, ct);

    public Task<ResultadoAtendimento> ExcluirCidadeAsync(Guid id, int? revisao, Guid autor, Guid chave, CancellationToken ct) =>
        ExecutarAsync("excluir-cidade", id, revisao, autor, chave, async _ =>
        {
            if (!RevisaoValida(revisao)) return Invalido("Informe a revisão da cidade.");
            var cidade = await Cidades.SingleOrDefaultAsync(c => c.Id == id, ct);
            if (cidade is null) return Ausente();
            if (cidade.Revisao != revisao) return RevisaoDivergente();
            if (cidade.Ativa && !await PermiteRemoverCidadeAtivaAsync(id, ct)) return UltimaModalidade();
            Cidades.Remove(cidade);
            return Sucesso(CidadeEntregaDto.De(cidade));
        }, ct);

    private async Task<bool> PermiteRemoverCidadeAtivaAsync(Guid id, CancellationToken ct) =>
        !await Configuracoes.AnyAsync(c => !c.RetiradaHabilitada, ct) ||
        await Cidades.AnyAsync(c => c.Ativa && c.Id != id, ct);

    private async Task<ConfiguracaoAtendimento> CarregarConfiguracaoAsync(CancellationToken ct) =>
        await Configuracoes.SingleOrDefaultAsync(ct) ?? Configuracoes.Add(new()).Entity;

    private async Task<ResultadoAtendimento> ExecutarAsync<T>(string operacao, Guid? cidadeId, T input,
        Guid autor, Guid chave, Func<DateTimeOffset, Task<ResultadoAtendimento>> acao, CancellationToken ct)
    {
        var impressao = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { operacao, cidadeId, input }, Json)));
        await using var transacao = await db.Database.BeginTransactionAsync(ct);
        // Todas as escritas compartilham o lock, inclusive quando a configuração ainda não existe.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @resultado int;
            EXEC @resultado = sys.sp_getapplock @Resource = {RecursoLock},
                @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 10000;
            IF @resultado < 0 THROW 51000, 'Store configuration lock unavailable.', 1;
            """, ct);
        var operacoes = db.Set<OperacaoAtendimento>();
        var anterior = await operacoes.AsNoTracking().SingleOrDefaultAsync(o => o.Autor == autor && o.Chave == chave, ct);
        if (anterior is not null)
            return anterior.Impressao == impressao
                ? Sucesso(JsonSerializer.Deserialize<JsonElement>(anterior.ResultadoJson))
                : Conflito("A chave de idempotência já foi usada para outro comando.");

        var agora = relogio.GetUtcNow();
        var resultado = await acao(agora);
        if (resultado.Status != StatusAtendimento.Sucesso)
        {
            db.ChangeTracker.Clear();
            return resultado;
        }

        operacoes.Add(new(autor, chave, impressao, operacao, cidadeId ?? (resultado.Dados as CidadeEntregaDto)?.Id,
            agora, JsonSerializer.Serialize(resultado.Dados, Json)));
        await db.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
        return resultado;
    }

    private static void AtualizarAuditoria(ConfiguracaoAtendimento config, Guid autor, DateTimeOffset agora)
    {
        config.Revisao++;
        config.AdministradorId = autor;
        config.AlteradoEmUtc = agora;
    }

    private static AtendimentoAdmin Mapear(ConfiguracaoAtendimento config) => new(config.RetiradaHabilitada,
        config.EntregaPropriaHabilitada, false, config.EnderecoRetirada, config.InstrucoesRetirada, config.WhatsApp,
        config.WhatsAppConfirmado, config.WhatsAppHabilitado, config.Revisao,
        config.Revisao == 0 ? null : config.AdministradorId, config.Revisao == 0 ? null : config.AlteradoEmUtc);

    private static string? NormalizarTelefone(string valor)
    {
        if (valor.Length > 40 || !FormaTelefone().IsMatch(valor)) return null;
        var digitos = new string(valor.Where(char.IsAsciiDigit).ToArray());
        if (digitos.Length is < 8 or > 15 || digitos[0] == '0') return null;
        if (digitos.StartsWith("55", StringComparison.Ordinal) &&
            (digitos.Length is not (12 or 13) || digitos[2] == '0' || digitos[3] == '0')) return null;
        return "+" + digitos;
    }

    [GeneratedRegex(@"^\+[1-9][0-9 ().-]*\z", RegexOptions.NonBacktracking)]
    private static partial Regex FormaTelefone();

    private static bool RevisaoValida(int? revisao) => revisao is >= 0 and < int.MaxValue;
    private static string? Texto(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
    private static ResultadoAtendimento Sucesso(object dados) => new(StatusAtendimento.Sucesso, Dados: dados);
    private static ResultadoAtendimento Invalido(string erro) => new(StatusAtendimento.Invalido, erro);
    private static ResultadoAtendimento Conflito(string erro) => new(StatusAtendimento.Conflito, erro);
    private static ResultadoAtendimento Ausente() => new(StatusAtendimento.Ausente);
    private static ResultadoAtendimento RevisaoDivergente() => Conflito("A revisão mudou. Consulte o estado atual.");
    private static ResultadoAtendimento UltimaModalidade() => Conflito("Mantenha ao menos uma modalidade operacional.");
}
