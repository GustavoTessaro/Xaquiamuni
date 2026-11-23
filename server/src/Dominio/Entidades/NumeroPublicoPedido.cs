using System.Security.Cryptography;

namespace Xaquiamuni.Dominio.Entidades;

public static class NumeroPublicoPedido
{
    public const string Alfabeto = "23456789ACDEFHJKMNPQRTUVWXY";
    private const string Prefixo = "XQ-";
    private const int TamanhoSufixo = 10;

    public static string Gerar() => Prefixo + RandomNumberGenerator.GetString(Alfabeto, TamanhoSufixo);

    public static string Validar(string numero)
    {
        if (numero.Length != Prefixo.Length + TamanhoSufixo ||
            !numero.StartsWith(Prefixo, StringComparison.Ordinal) ||
            numero.AsSpan(Prefixo.Length).ContainsAnyExcept(Alfabeto))
            throw new ArgumentException("Número público de pedido inválido.", nameof(numero));

        return numero;
    }
}
