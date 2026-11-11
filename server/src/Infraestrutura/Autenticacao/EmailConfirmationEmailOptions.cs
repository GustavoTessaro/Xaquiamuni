namespace Xaquiamuni.Infraestrutura.Autenticacao;

public sealed class EmailConfirmationEmailOptions
{
    public const string SectionName = "EmailConfirmation";

    public string? PublicFrontendBaseUrl { get; set; }
}
