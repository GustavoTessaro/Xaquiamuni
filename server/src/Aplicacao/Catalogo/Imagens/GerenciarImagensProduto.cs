using System.Runtime.ExceptionServices;
using Xaquiamuni.Aplicacao.Imagens;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Aplicacao.Catalogo.Imagens;

public sealed class GerenciarImagensProduto : IGerenciarImagensProduto
{
    private readonly IProdutoRepository _repository;
    private readonly IProcessadorImagemProduto _processador;
    private readonly IArmazenamentoImagemProduto _armazenamento;
    private readonly IProdutoImagemOperacaoLogger _operacaoLogger;

    public GerenciarImagensProduto(
        IProdutoRepository repository,
        IProcessadorImagemProduto processador,
        IArmazenamentoImagemProduto armazenamento,
        IProdutoImagemOperacaoLogger operacaoLogger)
    {
        _repository = repository;
        _processador = processador;
        _armazenamento = armazenamento;
        _operacaoLogger = operacaoLogger;
    }

    public async Task<ProdutoImagemOperacaoResult> AdicionarAsync(
        Guid produtoId,
        AdicionarImagemProdutoInput? input,
        CancellationToken cancellationToken = default)
    {
        if (produtoId == Guid.Empty)
        {
            return ProdutoImagemOperacaoResult.ValidationFailed(
                "produtoId",
                "O identificador do produto é obrigatório.");
        }

        if (input is null || input.Arquivo is null || !input.Arquivo.CanRead)
        {
            return ProdutoImagemOperacaoResult.ValidationFailed(
                "arquivo",
                "O arquivo de imagem é obrigatório e deve permitir leitura.");
        }

        string nomeOriginal;
        try
        {
            nomeOriginal = ProdutoImagem.ValidarNomeOriginal(input.NomeOriginal);
        }
        catch (ArgumentException exception)
        {
            return ProdutoImagemOperacaoResult.ValidationFailed("nomeOriginal", exception.Message);
        }

        var varianteIds = input.VarianteIds?.ToArray() ?? [];
        if (varianteIds.Any(id => id == Guid.Empty)
            || varianteIds.Distinct().Count() != varianteIds.Length)
        {
            return ProdutoImagemOperacaoResult.ValidationFailed(
                "varianteIds",
                "As opções devem ser identificadas sem valores vazios ou duplicados.");
        }

        var produto = await _repository.ConsultarAsync(produtoId, cancellationToken)
            .ConfigureAwait(false);
        if (produto is null)
        {
            return ProdutoImagemOperacaoResult.ProductNotFound();
        }

        if (produto.Imagens.Count >= ProdutoImagem.QuantidadeMaximaPorProduto)
        {
            return ProdutoImagemOperacaoResult.Conflict(
                $"Um produto pode possuir no máximo {ProdutoImagem.QuantidadeMaximaPorProduto} imagens.");
        }

        if (varianteIds.Any(id => produto.Variantes.All(variante => variante.Id != id)))
        {
            return ProdutoImagemOperacaoResult.VariantNotFound();
        }

        ImagemProdutoProcessada processada;
        try
        {
            processada = await _processador.ProcessarAsync(input.Arquivo, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ProcessamentoImagemProdutoException exception)
        {
            return ProdutoImagemOperacaoResult.InvalidImage(exception.Message);
        }

        using (processada)
        {
            var chave = ChaveArmazenamentoImagemProduto.Gerar();
            try
            {
                await _armazenamento.ArmazenarAsync(chave, processada.Conteudo, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (IOException exception)
            {
                _operacaoLogger.RegistrarFalhaDeArmazenamento(
                    produtoId,
                    null,
                    chave,
                    "store",
                    exception);
                return ProdutoImagemOperacaoResult.StorageUnavailable();
            }
            catch (UnauthorizedAccessException exception)
            {
                _operacaoLogger.RegistrarFalhaDeArmazenamento(
                    produtoId,
                    null,
                    chave,
                    "store",
                    exception);
                return ProdutoImagemOperacaoResult.StorageUnavailable();
            }
            catch (OperationCanceledException exception)
            {
                _operacaoLogger.RegistrarFalhaDeArmazenamento(
                    produtoId,
                    null,
                    chave,
                    "store-cancelled",
                    exception);
                throw;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                var cancellation = new OperationCanceledException(cancellationToken);
                if (!await CompensarArmazenamentoAsync(produtoId, chave, cancellation)
                    .ConfigureAwait(false))
                {
                    throw new AggregateException(
                        "The upload was cancelled and its stored image could not be compensated.",
                        cancellation);
                }

                ExceptionDispatchInfo.Capture(cancellation).Throw();
            }

            ProdutoMutationExecutionResult execution;
            try
            {
                execution = await _repository.ExecutarMutacaoComResultadoAsync(
                    async (session, token) =>
                    {
                        var produtoAtual = await session.BuscarProdutoAsync(produtoId, token)
                            .ConfigureAwait(false);
                        if (produtoAtual is null)
                        {
                            return ProdutoResult.ProductNotFound();
                        }

                        foreach (var varianteId in varianteIds)
                        {
                            if (produtoAtual.Variantes.All(variante => variante.Id != varianteId))
                            {
                                return ProdutoResult.VariantNotFound();
                            }
                        }

                        try
                        {
                            var imagem = produtoAtual.AdicionarImagem(
                                chave,
                                nomeOriginal,
                                processada.TipoConteudo,
                                processada.TamanhoBytes,
                                processada.LarguraPixels,
                                processada.AlturaPixels);
                            foreach (var varianteId in varianteIds)
                            {
                                produtoAtual.AssociarImagemAVariante(imagem.Id, varianteId);
                            }

                            return ProdutoResult.Success(produtoAtual);
                        }
                        catch (InvalidOperationException exception)
                        {
                            return ProdutoResult.Conflict(exception.Message);
                        }
                        catch (ArgumentException exception)
                        {
                            return ProdutoResult.ValidationFailed(
                                new ProdutoValidationError("imagem", exception.Message));
                        }
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _operacaoLogger.RegistrarFalhaDeArmazenamento(
                    produtoId,
                    null,
                    chave,
                    "persist-outcome-unknown",
                    exception);
                throw;
            }

            if (execution.PersistenceState == ProdutoMutationPersistenceState.Unknown)
            {
                _operacaoLogger.RegistrarFalhaDeArmazenamento(
                    produtoId,
                    null,
                    chave,
                    "persist-outcome-unknown",
                    execution.Failure);
                if (execution.Failure is OperationCanceledException cancellation)
                {
                    ExceptionDispatchInfo.Capture(cancellation).Throw();
                }

                return ProdutoImagemOperacaoResult.CommitOutcomeUnknown();
            }

            if (execution.PersistenceState == ProdutoMutationPersistenceState.RolledBack)
            {
                if (!await CompensarArmazenamentoAsync(produtoId, chave, execution.Failure)
                    .ConfigureAwait(false))
                {
                    return ProdutoImagemOperacaoResult.CompensationPending();
                }

                if (execution.Failure is not null)
                {
                    ExceptionDispatchInfo.Capture(execution.Failure).Throw();
                }

                return MapearResultado(execution.Result!);
            }

            var imageResult = execution.Result
                ?? throw new InvalidOperationException("The committed image operation has no result.");
            return imageResult.Produto is null
                ? ProdutoImagemOperacaoResult.Conflict(imageResult.Mensagem)
                : ProdutoImagemOperacaoResult.Success(
                    ToDto(imageResult.Produto.Imagens.Single(image =>
                        image.ChaveArmazenamento == chave)),
                    MapearImagens(imageResult.Produto));
        }
    }

    public async Task<ProdutoImagemOperacaoResult> ListarAsync(
        Guid produtoId,
        CancellationToken cancellationToken = default)
    {
        if (produtoId == Guid.Empty)
        {
            return ProdutoImagemOperacaoResult.ValidationFailed(
                "produtoId",
                "O identificador do produto é obrigatório.");
        }

        var produto = await _repository.ConsultarAsync(produtoId, cancellationToken)
            .ConfigureAwait(false);
        return produto is null
            ? ProdutoImagemOperacaoResult.ProductNotFound()
            : ProdutoImagemOperacaoResult.Success(imagens: MapearImagens(produto));
    }

    public async Task<ProdutoImagemOperacaoResult> ConsultarAsync(
        Guid produtoId,
        Guid imagemId,
        CancellationToken cancellationToken = default)
    {
        if (produtoId == Guid.Empty || imagemId == Guid.Empty)
        {
            return ProdutoImagemOperacaoResult.ValidationFailed(
                "imagemId",
                "Os identificadores do produto e da imagem são obrigatórios.");
        }

        var produto = await _repository.ConsultarAsync(produtoId, cancellationToken)
            .ConfigureAwait(false);
        if (produto is null)
        {
            return ProdutoImagemOperacaoResult.ProductNotFound();
        }

        var imagem = produto.Imagens.SingleOrDefault(item => item.Id == imagemId);
        return imagem is null
            ? ProdutoImagemOperacaoResult.ImageNotFound()
            : ProdutoImagemOperacaoResult.Success(ToDto(imagem));
    }

    public async Task<ProdutoImagemConteudoResult> AbrirConteudoAsync(
        Guid produtoId,
        Guid imagemId,
        CancellationToken cancellationToken = default)
        => await AbrirConteudoInternoAsync(
            produtoId,
            imagemId,
            exigirVisibilidadePublica: false,
            cancellationToken).ConfigureAwait(false);

    public async Task<ProdutoImagemConteudoResult> AbrirConteudoPublicoAsync(
        Guid produtoId,
        Guid imagemId,
        CancellationToken cancellationToken = default)
        => await AbrirConteudoInternoAsync(
            produtoId,
            imagemId,
            exigirVisibilidadePublica: true,
            cancellationToken).ConfigureAwait(false);

    private async Task<ProdutoImagemConteudoResult> AbrirConteudoInternoAsync(
        Guid produtoId,
        Guid imagemId,
        bool exigirVisibilidadePublica,
        CancellationToken cancellationToken)
    {
        if (produtoId == Guid.Empty || imagemId == Guid.Empty)
        {
            return ProdutoImagemConteudoResult.ValidationFailed(
                "Os identificadores do produto e da imagem são obrigatórios.");
        }

        var produto = await _repository.ConsultarAsync(produtoId, cancellationToken)
            .ConfigureAwait(false);
        if (produto is null)
        {
            return ProdutoImagemConteudoResult.ProductNotFound();
        }

        if (exigirVisibilidadePublica && (!produto.Publicado || !produto.Categoria.Ativa))
        {
            return ProdutoImagemConteudoResult.ProductNotFound();
        }

        var imagem = produto.Imagens.SingleOrDefault(item => item.Id == imagemId);
        if (imagem is null)
        {
            return ProdutoImagemConteudoResult.ImageNotFound();
        }

        try
        {
            var stream = await _armazenamento.AbrirLeituraAsync(
                    imagem.ChaveArmazenamento,
                    cancellationToken)
                .ConfigureAwait(false);
            return stream is null
                ? ProdutoImagemConteudoResult.StoredFileMissing()
                : ProdutoImagemConteudoResult.Success(ToDto(imagem), stream);
        }
        catch (IOException exception)
        {
            _operacaoLogger.RegistrarFalhaDeArmazenamento(
                produtoId,
                imagemId,
                imagem.ChaveArmazenamento,
                "read",
                exception);
            return ProdutoImagemConteudoResult.StorageUnavailable();
        }
        catch (UnauthorizedAccessException exception)
        {
            _operacaoLogger.RegistrarFalhaDeArmazenamento(
                produtoId,
                imagemId,
                imagem.ChaveArmazenamento,
                "read",
                exception);
            return ProdutoImagemConteudoResult.StorageUnavailable();
        }
    }

    public Task<ProdutoImagemOperacaoResult> DefinirPrincipalAsync(
        Guid produtoId,
        Guid imagemId,
        CancellationToken cancellationToken = default) =>
        imagemId == Guid.Empty
            ? Task.FromResult(ProdutoImagemOperacaoResult.ValidationFailed(
                "imagemId",
                "O identificador da imagem é obrigatório."))
            : ExecutarMutacaoAsync(
            produtoId,
            produto =>
            {
                if (produto.Imagens.All(imagem => imagem.Id != imagemId))
                {
                    return ProdutoResult.ImageNotFound();
                }

                produto.DefinirImagemPrincipal(imagemId);
                return ProdutoResult.Success(produto);
            },
            cancellationToken);

    public Task<ProdutoImagemOperacaoResult> ReordenarAsync(
        Guid produtoId,
        IReadOnlyList<Guid>? imagemIds,
        CancellationToken cancellationToken = default) =>
        imagemIds is null
            ? Task.FromResult(ProdutoImagemOperacaoResult.ValidationFailed(
                "imagemIds",
                "A nova ordem das imagens é obrigatória."))
            : ExecutarMutacaoAsync(
                produtoId,
                produto =>
                {
                    try
                    {
                        produto.ReordenarImagens(imagemIds);
                        return ProdutoResult.Success(produto);
                    }
                    catch (ArgumentException exception)
                    {
                        return ProdutoResult.ValidationFailed(
                            new ProdutoValidationError("imagemIds", exception.Message));
                    }
                },
                cancellationToken);

    public Task<ProdutoImagemOperacaoResult> AssociarVarianteAsync(
        Guid produtoId,
        Guid imagemId,
        Guid varianteId,
        CancellationToken cancellationToken = default) =>
        imagemId == Guid.Empty || varianteId == Guid.Empty
            ? Task.FromResult(ProdutoImagemOperacaoResult.ValidationFailed(
                "imagemId",
                "Os identificadores da imagem e da opção são obrigatórios."))
            : ExecutarMutacaoAsync(
            produtoId,
            produto =>
            {
                if (produto.Imagens.All(imagem => imagem.Id != imagemId))
                {
                    return ProdutoResult.ImageNotFound();
                }

                if (produto.Variantes.All(variante => variante.Id != varianteId))
                {
                    return ProdutoResult.VariantNotFound();
                }

                try
                {
                    produto.AssociarImagemAVariante(imagemId, varianteId);
                    return ProdutoResult.Success(produto);
                }
                catch (InvalidOperationException exception)
                {
                    return ProdutoResult.Conflict(exception.Message);
                }
                catch (ArgumentException exception)
                {
                    return ProdutoResult.ValidationFailed(
                        new ProdutoValidationError("varianteId", exception.Message));
                }
            },
            cancellationToken);

    public Task<ProdutoImagemOperacaoResult> DesassociarVarianteAsync(
        Guid produtoId,
        Guid imagemId,
        Guid varianteId,
        CancellationToken cancellationToken = default) =>
        imagemId == Guid.Empty || varianteId == Guid.Empty
            ? Task.FromResult(ProdutoImagemOperacaoResult.ValidationFailed(
                "imagemId",
                "Os identificadores da imagem e da opção são obrigatórios."))
            : ExecutarMutacaoAsync(
            produtoId,
            produto =>
            {
                if (produto.Imagens.All(imagem => imagem.Id != imagemId))
                {
                    return ProdutoResult.ImageNotFound();
                }

                if (produto.Variantes.All(variante => variante.Id != varianteId))
                {
                    return ProdutoResult.VariantNotFound();
                }

                try
                {
                    produto.DesassociarImagemDaVariante(imagemId, varianteId);
                    return ProdutoResult.Success(produto);
                }
                catch (ArgumentException exception)
                {
                    return ProdutoResult.ValidationFailed(
                        new ProdutoValidationError("varianteId", exception.Message));
                }
            },
            cancellationToken);

    public async Task<ProdutoImagemOperacaoResult> RemoverAsync(
        Guid produtoId,
        Guid imagemId,
        CancellationToken cancellationToken = default)
    {
        if (imagemId == Guid.Empty)
        {
            return ProdutoImagemOperacaoResult.ValidationFailed(
                "imagemId",
                "O identificador da imagem é obrigatório.");
        }

        string? chave = null;
        var result = await ExecutarMutacaoAsync(
                produtoId,
                produto =>
                {
                    var imagem = produto.Imagens.SingleOrDefault(item => item.Id == imagemId);
                    if (imagem is null)
                    {
                        return ProdutoResult.ImageNotFound();
                    }

                    chave = imagem.ChaveArmazenamento;
                    try
                    {
                        produto.RemoverImagem(imagemId);
                        return ProdutoResult.Success(produto);
                    }
                    catch (InvalidOperationException exception)
                    {
                        return ProdutoResult.Conflict(exception.Message);
                    }
                },
                cancellationToken,
                () => chave)
            .ConfigureAwait(false);

        return result.Status == ProdutoImagemOperacaoStatus.Success
            ? ProdutoImagemOperacaoResult.CleanupPending()
            : result;
    }

    private async Task<ProdutoImagemOperacaoResult> ExecutarMutacaoAsync(
        Guid produtoId,
        Func<Produto, ProdutoResult> operacao,
        CancellationToken cancellationToken,
        Func<string?>? obterChaveArmazenamento = null)
    {
        if (produtoId == Guid.Empty)
        {
            return ProdutoImagemOperacaoResult.ValidationFailed(
                "produtoId",
                "O identificador do produto é obrigatório.");
        }

        var execution = await _repository.ExecutarMutacaoComResultadoAsync(
            async (session, token) =>
            {
                var produto = await session.BuscarProdutoAsync(produtoId, token).ConfigureAwait(false);
                if (produto is null)
                {
                    return ProdutoResult.ProductNotFound();
                }

                try
                {
                    return operacao(produto);
                }
                catch (InvalidOperationException exception)
                {
                    return ProdutoResult.Conflict(exception.Message);
                }
                catch (ArgumentException exception)
                {
                    return ProdutoResult.ValidationFailed(
                        new ProdutoValidationError("imagem", exception.Message));
                }
            },
            cancellationToken).ConfigureAwait(false);

        if (execution.PersistenceState == ProdutoMutationPersistenceState.Unknown)
        {
            if (execution.Failure is not null)
            {
                _operacaoLogger.RegistrarFalhaDeArmazenamento(
                    produtoId,
                    null,
                    obterChaveArmazenamento?.Invoke(),
                    "mutation-outcome-unknown",
                    execution.Failure);
                if (execution.Failure is OperationCanceledException cancellation)
                {
                    ExceptionDispatchInfo.Capture(cancellation).Throw();
                }
            }
            return ProdutoImagemOperacaoResult.CommitOutcomeUnknown();
        }

        if (execution.PersistenceState == ProdutoMutationPersistenceState.RolledBack)
        {
            if (execution.Failure is not null)
            {
                ExceptionDispatchInfo.Capture(execution.Failure).Throw();
            }

            return MapearResultado(execution.Result!);
        }

        var committedResult = execution.Result
            ?? throw new InvalidOperationException("The committed image operation has no result.");
        return committedResult.Produto is null
            ? MapearResultado(committedResult)
            : ProdutoImagemOperacaoResult.Success(imagens: MapearImagens(committedResult.Produto));
    }

    private async Task<bool> CompensarArmazenamentoAsync(
        Guid produtoId,
        string chave,
        Exception? causa)
    {
        try
        {
            await _armazenamento.ExcluirAsync(chave, CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception)
        {
            _operacaoLogger.RegistrarFalhaDeArmazenamento(
                produtoId,
                null,
                chave,
                "compensation",
                causa is null ? exception : new AggregateException(causa, exception));
            return false;
        }
    }

    private static ProdutoImagemOperacaoResult MapearResultado(ProdutoResult result) =>
        result.Status switch
        {
            ProdutoStatus.Success when result.Produto is not null =>
                ProdutoImagemOperacaoResult.Success(imagens: MapearImagens(result.Produto)),
            ProdutoStatus.ProductNotFound => ProdutoImagemOperacaoResult.ProductNotFound(),
            ProdutoStatus.ImageNotFound => ProdutoImagemOperacaoResult.ImageNotFound(),
            ProdutoStatus.VariantNotFound => ProdutoImagemOperacaoResult.VariantNotFound(),
            ProdutoStatus.ValidationFailed when result.Erros.Count > 0 =>
                ProdutoImagemOperacaoResult.ValidationFailed(
                    result.Erros[0].Campo,
                    result.Erros[0].Mensagem),
            ProdutoStatus.Conflict or ProdutoStatus.CategoryInactive =>
                ProdutoImagemOperacaoResult.Conflict(result.Mensagem),
            _ => ProdutoImagemOperacaoResult.Conflict()
        };

    private static IReadOnlyList<ProdutoImagemDto> MapearImagens(Produto produto) =>
        produto.Imagens
            .OrderBy(imagem => imagem.OrdemExibicao)
            .Select(ToDto)
            .ToArray();

    private static ProdutoImagemDto ToDto(ProdutoImagem imagem) =>
        new(
            imagem.Id,
            imagem.ProdutoId,
            imagem.NomeOriginal,
            imagem.TipoConteudo,
            imagem.TamanhoBytes,
            imagem.LarguraPixels,
            imagem.AlturaPixels,
            imagem.OrdemExibicao,
            imagem.Principal,
            imagem.Variantes
                .Select(associacao => associacao.ProdutoVarianteId)
                .Order()
                .ToArray(),
            imagem.CriadoEmUtc);
}
