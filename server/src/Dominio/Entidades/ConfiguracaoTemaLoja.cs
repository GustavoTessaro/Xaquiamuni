namespace Xaquiamuni.Dominio.Entidades;

public sealed class ConfiguracaoTemaLoja
{
    public const int IdUnico = 1;
    public int Id { get; private set; } = IdUnico;
    public string Chave { get; private set; } = null!;
    public int Revisao { get; private set; }
    public DateTimeOffset PublicadoEmUtc { get; private set; }
    public Guid AdministradorId { get; private set; }
    public Guid UltimaChaveIdempotencia { get; private set; }

    public void Publicar(string chave, Guid administradorId, Guid idempotencia, DateTimeOffset agora)
    {
        Revisao = checked(Revisao + 1);
        Chave = chave;
        AdministradorId = administradorId;
        UltimaChaveIdempotencia = idempotencia;
        PublicadoEmUtc = agora.ToUniversalTime();
    }
}
