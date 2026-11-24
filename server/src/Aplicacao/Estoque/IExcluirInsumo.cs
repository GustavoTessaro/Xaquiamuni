using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Xaquiamuni.Dominio.Estoque;

namespace Xaquiamuni.Aplicacao.Estoque;

public interface IExcluirInsumo
{
    Task<(int Status, string? Codigo)> ExecutarAsync(Guid insumo, Guid? forma, string versao,
        Guid autor, Guid chave, CancellationToken ct);
}

public static class VersaoCadastroInsumo
{
    public static string Calcular(DetalheEstoqueInsumo detalhe)
    {
        var resumo = detalhe.Resumo;
        return Calcular(resumo.InsumoId, resumo.Nome, resumo.Tipo, resumo.UnidadeBase, resumo.Ativo,
            resumo.ControlaLote, resumo.ValidadeObrigatoria, resumo.EstoqueMinimo, detalhe.FormasCompra);
    }

    public static string Calcular(InsumoEstoque insumo) =>
        Calcular(insumo.Id, insumo.Nome, insumo.Tipo, insumo.UnidadeBase, insumo.Ativo,
            insumo.ControlaLote, insumo.ValidadeObrigatoria, insumo.EstoqueMinimo?.ValorBase,
            insumo.FormasCompra.Select(f => new FormaCompraInsumoConsulta(f.Id, f.Nome,
                f.QuantidadeEquivalentePorEmbalagem.ValorBase, f.QuantidadeEquivalentePorEmbalagem.UnidadeBase, f.Ativa)));

    private static string Calcular(Guid id, string nome, TipoInsumoEstoque tipo, UnidadeFisicaEstoque unidade,
        bool ativo, bool controlaLote, bool validade, decimal? minimo, IEnumerable<FormaCompraInsumoConsulta> formas) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            id, nome, tipo, unidade, ativo, controlaLote, validade,
            minimo = minimo?.ToString("G29", CultureInfo.InvariantCulture),
            formas = formas.OrderBy(f => f.Id).Select(f => new
            {
                f.Id, f.Nome, f.Ativa, f.UnidadeBase,
                fator = f.QuantidadeEquivalentePorEmbalagem.ToString("G29", CultureInfo.InvariantCulture)
            })
        })));
}
