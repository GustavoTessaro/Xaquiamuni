using System.Text;
using Xaquiamuni.Dominio.Valores;

namespace Xaquiamuni.Dominio.Entidades;

public sealed class ClientePerfil
{
    private const int NomeCompletoMaxLength = 150;

    private ClientePerfil()
    {
    }

    private ClientePerfil(Guid userId, string nomeCompleto, CpfProtegido cpfProtegido)
    {
        UserId = userId;
        NomeCompleto = nomeCompleto;
        CpfProtegido = cpfProtegido;
    }

    public Guid UserId { get; private set; }
    public string NomeCompleto { get; private set; } = null!;
    public CpfProtegido CpfProtegido { get; private set; } = null!;

    public static ClientePerfil Criar(Guid userId, string? nomeCompleto, CpfProtegido cpfProtegido)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("O identificador do usuário é obrigatório.", nameof(userId));
        }

        ArgumentNullException.ThrowIfNull(cpfProtegido);
        return new ClientePerfil(userId, NormalizarNome(nomeCompleto), cpfProtegido);
    }

    public void AtualizarNomeCompleto(string? nomeCompleto)
    {
        NomeCompleto = NormalizarNome(nomeCompleto);
    }

    public override string ToString() => "[ClientePerfil]";

    private static string NormalizarNome(string? nomeCompleto)
    {
        if (string.IsNullOrWhiteSpace(nomeCompleto))
        {
            throw new ArgumentException("O nome completo é obrigatório.", nameof(nomeCompleto));
        }

        var normalized = new StringBuilder(nomeCompleto.Length);
        var pendingSpace = false;
        foreach (var character in nomeCompleto.Trim())
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = normalized.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                normalized.Append(' ');
                pendingSpace = false;
            }

            normalized.Append(character);
            if (normalized.Length > NomeCompletoMaxLength)
            {
                throw new ArgumentException("O nome completo excede o limite permitido.", nameof(nomeCompleto));
            }
        }

        if (normalized.Length == 0)
        {
            throw new ArgumentException("O nome completo é obrigatório.", nameof(nomeCompleto));
        }

        return normalized.ToString();
    }
}
