using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xaquiamuni.Aplicacao.Imagens;

namespace Xaquiamuni.Infraestrutura.Imagens;

public sealed class ArmazenamentoLocalImagemProduto : IArmazenamentoImagemProduto
{
    private const string DiretorioImagens = "product-images";
    private const int TamanhoBuffer = 81920;
    private readonly string _diretorioRaiz;
    private readonly string _diretorioImagens;

    public ArmazenamentoLocalImagemProduto(
        IOptions<ArmazenamentoLocalImagemProdutoOptions> options,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);

        var rootPath = options.Value.RootPath;
        if (string.IsNullOrWhiteSpace(rootPath) || !Path.IsPathFullyQualified(rootPath))
        {
            throw new InvalidOperationException(
                "O diretório de armazenamento de imagens deve ser um caminho absoluto.");
        }

        _diretorioRaiz = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var raizRepositorio = EncontrarRaizRepositorio(environment.ContentRootPath);
        if (ContemSegmentoWebRoot(_diretorioRaiz)
            || EstaDentroDe(_diretorioRaiz, environment.ContentRootPath)
            || (raizRepositorio is not null && EstaDentroDe(_diretorioRaiz, raizRepositorio))
            || EstaDentroDe(environment.ContentRootPath, _diretorioRaiz))
        {
            throw new InvalidOperationException(
                "O armazenamento local de imagens deve ficar fora dos arquivos da aplicação e de wwwroot.");
        }

        _diretorioImagens = Path.Combine(_diretorioRaiz, DiretorioImagens);
    }

    public async Task ArmazenarAsync(
        string chave,
        Stream conteudo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conteudo);
        if (!conteudo.CanRead)
        {
            throw new ArgumentException("O conteúdo deve permitir leitura.", nameof(conteudo));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var destino = ObterCaminho(chave);
        Directory.CreateDirectory(_diretorioRaiz);
        GarantirDiretorioSeguro(_diretorioRaiz);
        Directory.CreateDirectory(_diretorioImagens);
        GarantirDiretorioSeguro(_diretorioImagens);

        var temporario = Path.Combine(_diretorioImagens, $".{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var arquivo = new FileStream(
                temporario,
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    BufferSize = TamanhoBuffer,
                    Options = FileOptions.Asynchronous | FileOptions.WriteThrough
                }))
            {
                await conteudo.CopyToAsync(arquivo, TamanhoBuffer, cancellationToken);
                await arquivo.FlushAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            GarantirArquivoSeguro(destino);
            File.Move(temporario, destino, overwrite: false);
        }
        catch (Exception erroDaGravacao)
        {
            try
            {
                File.Delete(temporario);
            }
            catch (Exception erroDaLimpeza)
            {
                throw new AggregateException(
                    "Falha ao armazenar a imagem e ao limpar o arquivo temporário.",
                    erroDaGravacao,
                    erroDaLimpeza);
            }

            throw;
        }
    }

    public Task<Stream?> AbrirLeituraAsync(
        string chave,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var caminho = ObterCaminho(chave);
        try
        {
            GarantirArquivoSeguro(caminho);
            Stream stream = new FileStream(
                caminho,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                TamanhoBuffer,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return Task.FromResult<Stream?>(stream);
        }
        catch (FileNotFoundException)
        {
            return Task.FromResult<Stream?>(null);
        }
        catch (DirectoryNotFoundException)
        {
            return Task.FromResult<Stream?>(null);
        }
    }

    public Task<bool> ExisteAsync(string chave, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var caminho = ObterCaminho(chave);
        try
        {
            GarantirArquivoSeguro(caminho);
            _ = File.GetAttributes(caminho);
            return Task.FromResult(true);
        }
        catch (FileNotFoundException)
        {
            return Task.FromResult(false);
        }
        catch (DirectoryNotFoundException)
        {
            return Task.FromResult(false);
        }
    }

    public Task ExcluirAsync(string chave, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var caminho = ObterCaminho(chave);
        try
        {
            GarantirArquivoSeguro(caminho);
            File.Delete(caminho);
        }
        catch (FileNotFoundException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }

        return Task.CompletedTask;
    }

    private string ObterCaminho(string chave)
    {
        if (!ChaveArmazenamentoImagemProduto.EhValida(chave))
        {
            throw new ArgumentException(
                "A chave deve ser uma chave de imagem gerada pelo sistema.",
                nameof(chave));
        }

        var nome = chave[(chave.LastIndexOf('/') + 1)..];
        var caminho = Path.GetFullPath(Path.Combine(_diretorioImagens, nome));
        if (!EstaDentroDe(caminho, _diretorioImagens))
        {
            throw new ArgumentException("A chave escapa do diretório de armazenamento.", nameof(chave));
        }

        return caminho;
    }

    private static void GarantirDiretorioSeguro(string caminho)
    {
        if ((File.GetAttributes(caminho) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("O diretório de armazenamento não pode ser um link ou ponto de nova análise.");
        }
    }

    private static void GarantirArquivoSeguro(string caminho)
    {
        if (!File.Exists(caminho))
        {
            return;
        }

        if ((File.GetAttributes(caminho) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("O objeto de armazenamento não pode ser um link ou ponto de nova análise.");
        }
    }

    private static bool ContemSegmentoWebRoot(string caminho) =>
        caminho.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries)
            .Any(segmento => string.Equals(segmento, "wwwroot", StringComparison.OrdinalIgnoreCase));

    private static string? EncontrarRaizRepositorio(string contentRoot)
    {
        DirectoryInfo? diretorio = new(Path.GetFullPath(contentRoot));
        while (diretorio is not null)
        {
            if (Directory.Exists(Path.Combine(diretorio.FullName, ".git"))
                || File.Exists(Path.Combine(diretorio.FullName, ".git")))
            {
                return diretorio.FullName;
            }

            diretorio = diretorio.Parent;
        }

        return null;
    }

    private static bool EstaDentroDe(string caminho, string diretorio)
    {
        var raiz = Path.TrimEndingDirectorySeparator(Path.GetFullPath(diretorio));
        var caminhoCompleto = Path.GetFullPath(caminho);
        var relativo = Path.GetRelativePath(raiz, caminhoCompleto);
        return relativo == "."
            || (!Path.IsPathRooted(relativo)
                && relativo != ".."
                && !relativo.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !relativo.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal));
    }
}
