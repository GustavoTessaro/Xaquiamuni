namespace Xaquiamuni.Infraestrutura.Imagens;

public sealed class ArmazenamentoLocalImagemProdutoOptions
{
    public const string SectionName = "ProductImages:Storage";

    public string RootPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Xaquiamuni");
}
