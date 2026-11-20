namespace Xaquiamuni.Dominio.Agenda;

public sealed class CompromissoAgenda
{
    public const int TituloMaximo = 120;
    public const int DescricaoMaxima = 2000;
    public const int CategoriaMaxima = 80;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid Versao { get; private set; }
    public string Titulo { get; private set; } = null!;
    public string? Descricao { get; private set; }
    public string? Categoria { get; private set; }
    public DateOnly Data { get; private set; }
    public TimeOnly? HorarioInicial { get; private set; }
    public TimeOnly? HorarioFinal { get; private set; }
    public Guid CriadoPorId { get; private set; }
    public Guid AtualizadoPorId { get; private set; }
    public DateTimeOffset CriadoEmUtc { get; private set; }
    public DateTimeOffset AtualizadoEmUtc { get; private set; }
    public DateTimeOffset? ExcluidoEmUtc { get; private set; }
    public Guid? ExcluidoPorId { get; private set; }

    private CompromissoAgenda() { }

    public static CompromissoAgenda Criar(string? titulo, string? descricao, string? categoria,
        DateOnly data, TimeOnly? inicio, TimeOnly? fim, Guid autor, DateTimeOffset agora)
    {
        var compromisso = new CompromissoAgenda { CriadoPorId = autor, CriadoEmUtc = agora.ToUniversalTime() };
        compromisso.Alterar(titulo, descricao, categoria, data, inicio, fim, autor, agora);
        return compromisso;
    }

    public void Alterar(string? titulo, string? descricao, string? categoria, DateOnly data,
        TimeOnly? inicio, TimeOnly? fim, Guid autor, DateTimeOffset agora)
    {
        ExigirAtivo();
        ValidarAuditoria(autor, agora);
        if (data == default) throw new ArgumentException("Informe a data do compromisso.");
        if (fim.HasValue && (!inicio.HasValue || fim <= inicio))
            throw new ArgumentException("O horário final exige um horário inicial e deve ser posterior a ele.");

        var tituloValidado = Texto(titulo, TituloMaximo) ?? throw new ArgumentException("Informe o título.");
        var descricaoValidada = Texto(descricao, DescricaoMaxima);
        var categoriaValidada = Texto(categoria, CategoriaMaxima);
        Titulo = tituloValidado;
        Descricao = descricaoValidada;
        Categoria = categoriaValidada;
        Data = data;
        HorarioInicial = inicio;
        HorarioFinal = fim;
        AtualizarAuditoria(autor, agora);
    }

    public void Excluir(Guid autor, DateTimeOffset agora)
    {
        ExigirAtivo();
        ValidarAuditoria(autor, agora);
        ExcluidoPorId = autor;
        ExcluidoEmUtc = agora.ToUniversalTime();
        AtualizarAuditoria(autor, agora);
    }

    private void ExigirAtivo()
    {
        if (ExcluidoEmUtc.HasValue) throw new InvalidOperationException("O compromisso foi excluído.");
    }

    private void ValidarAuditoria(Guid autor, DateTimeOffset agora)
    {
        if (autor == Guid.Empty || agora < CriadoEmUtc || agora < AtualizadoEmUtc)
            throw new ArgumentException("Responsável ou data de auditoria inválidos.");
    }

    private void AtualizarAuditoria(Guid autor, DateTimeOffset agora)
    {
        AtualizadoPorId = autor;
        AtualizadoEmUtc = agora.ToUniversalTime();
        Versao = Guid.NewGuid();
    }

    private static string? Texto(string? texto, int limite)
    {
        texto = texto?.Trim();
        if (texto?.Length > limite) throw new ArgumentException($"O texto deve ter até {limite} caracteres.");
        return string.IsNullOrEmpty(texto) ? null : texto;
    }
}
