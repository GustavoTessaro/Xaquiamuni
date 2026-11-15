using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Aplicacao.Catalogo;

public sealed class GerenciarProdutos : IGerenciarProdutos
{
    private readonly IProdutoRepository _repository;

    public GerenciarProdutos(IProdutoRepository repository)
    {
        _repository = repository;
    }

    public Task<IReadOnlyList<Produto>> ListarAsync(
        ProdutoFiltro filtro,
        CancellationToken cancellationToken = default) =>
        _repository.ListarAsync(filtro, cancellationToken);

    public Task<Produto?> ConsultarAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        _repository.ConsultarAsync(id, cancellationToken);

    public async Task<ProdutoResult> CadastrarAsync(
        ProdutoCadastroInput? input,
        CancellationToken cancellationToken = default)
    {
        if (input is null)
        {
            return Invalid("produto", "Os dados do produto são obrigatórios.");
        }

        if (input.CategoriaId is null || input.CategoriaId.Value == Guid.Empty)
        {
            return Invalid("categoriaId", "A categoria é obrigatória.");
        }

        var creationError = CriarVariantes(input, out var variantes);
        if (creationError is not null)
        {
            return creationError;
        }

        return await _repository.ExecutarMutacaoAsync(async (session, token) =>
        {
            var categoria = await session.BuscarCategoriaAsync(input.CategoriaId!.Value, token)
                .ConfigureAwait(false);
            if (categoria is null)
            {
                return ProdutoResult.CategoryNotFound();
            }

            if (!categoria.Ativa)
            {
                return ProdutoResult.CategoryInactive();
            }

            try
            {
                var produto = Produto.Criar(input.Nome, input.Descricao, categoria, variantes);
                session.Adicionar(produto);
                return ProdutoResult.Success(produto);
            }
            catch (InvalidOperationException exception)
            {
                return ProdutoResult.Conflict(exception.Message);
            }
            catch (ArgumentException exception)
            {
                return Invalid(exception);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProdutoResult> EditarAsync(
        Guid id,
        ProdutoEdicaoInput? input,
        CancellationToken cancellationToken = default)
    {
        if (input is null)
        {
            return Invalid("produto", "Os dados do produto são obrigatórios.");
        }

        if (input.CategoriaId is null || input.CategoriaId.Value == Guid.Empty)
        {
            return Invalid("categoriaId", "A categoria é obrigatória.");
        }

        return await _repository.ExecutarMutacaoAsync(async (session, token) =>
        {
            var produto = await session.BuscarProdutoAsync(id, token).ConfigureAwait(false);
            if (produto is null)
            {
                return ProdutoResult.ProductNotFound();
            }

            Categoria? categoria = null;
            if (produto.CategoriaId != input.CategoriaId!.Value)
            {
                categoria = await session.BuscarCategoriaAsync(input.CategoriaId.Value, token)
                    .ConfigureAwait(false);
                if (categoria is null)
                {
                    return ProdutoResult.CategoryNotFound();
                }

                if (!categoria.Ativa)
                {
                    return ProdutoResult.CategoryInactive();
                }
            }

            try
            {
                produto.AtualizarDados(input.Nome, input.Descricao);
                if (categoria is not null)
                {
                    produto.AlterarCategoria(categoria);
                }

                return ProdutoResult.Success(produto);
            }
            catch (ArgumentException exception)
            {
                return Invalid(exception);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProdutoResult> AdicionarVarianteAsync(
        Guid produtoId,
        ProdutoVarianteInput? input,
        CancellationToken cancellationToken = default)
    {
        if (input is null)
        {
            return Invalid("variante", "Os dados da variante são obrigatórios.");
        }

        ProdutoVariante variante;
        try
        {
            variante = CriarVariante(input, padrao: false);
        }
        catch (ArgumentException exception)
        {
            return Invalid(exception);
        }

        return await _repository.ExecutarMutacaoAsync(async (session, token) =>
        {
            var produto = await session.BuscarProdutoAsync(produtoId, token).ConfigureAwait(false);
            if (produto is null)
            {
                return ProdutoResult.ProductNotFound();
            }

            try
            {
                produto.AdicionarVariante(variante);
                return ProdutoResult.Success(produto);
            }
            catch (InvalidOperationException exception)
            {
                return ProdutoResult.Conflict(exception.Message);
            }
            catch (ArgumentException exception)
            {
                return Invalid(exception);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProdutoResult> EditarVarianteAsync(
        Guid produtoId,
        Guid varianteId,
        ProdutoVarianteInput? input,
        CancellationToken cancellationToken = default)
    {
        if (input is null)
        {
            return Invalid("variante", "Os dados da variante são obrigatórios.");
        }

        return await _repository.ExecutarMutacaoAsync(async (session, token) =>
        {
            var produto = await session.BuscarProdutoAsync(produtoId, token).ConfigureAwait(false);
            if (produto is null)
            {
                return ProdutoResult.ProductNotFound();
            }

            if (produto.Variantes.All(variante => variante.Id != varianteId))
            {
                return ProdutoResult.VariantNotFound();
            }

            try
            {
                produto.AtualizarVariante(
                    varianteId,
                    input.Identificacao,
                    input.UnidadeVenda,
                    input.PrecoVenda);
                return ProdutoResult.Success(produto);
            }
            catch (InvalidOperationException exception)
            {
                return ProdutoResult.Conflict(exception.Message);
            }
            catch (ArgumentException exception)
            {
                return Invalid(exception);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProdutoResult> DefinirVariantePadraoAsync(
        Guid produtoId,
        Guid varianteId,
        CancellationToken cancellationToken = default) =>
        await _repository.ExecutarMutacaoAsync(async (session, token) =>
        {
            var produto = await session.BuscarProdutoAsync(produtoId, token).ConfigureAwait(false);
            if (produto is null)
            {
                return ProdutoResult.ProductNotFound();
            }

            if (produto.Variantes.All(variante => variante.Id != varianteId))
            {
                return ProdutoResult.VariantNotFound();
            }

            try
            {
                produto.DefinirVariantePadrao(varianteId);
                return ProdutoResult.Success(produto);
            }
            catch (InvalidOperationException exception)
            {
                return ProdutoResult.Conflict(exception.Message);
            }
            catch (ArgumentException)
            {
                return ProdutoResult.VariantNotFound();
            }
        }, cancellationToken).ConfigureAwait(false);

    public async Task<ProdutoResult> AlterarStatusVarianteAsync(
        Guid produtoId,
        Guid varianteId,
        bool? ativa,
        Guid? novoPadraoId,
        CancellationToken cancellationToken = default)
    {
        if (ativa is null)
        {
            return Invalid("ativa", "O estado da variante é obrigatório.");
        }

        if (ativa.Value && novoPadraoId is not null)
        {
            return Invalid("novoPadraoId", "A nova variante padrão só é informada ao desativar a padrão atual.");
        }

        return await _repository.ExecutarMutacaoAsync(async (session, token) =>
        {
            var produto = await session.BuscarProdutoAsync(produtoId, token).ConfigureAwait(false);
            if (produto is null)
            {
                return ProdutoResult.ProductNotFound();
            }

            if (produto.Variantes.All(variante => variante.Id != varianteId))
            {
                return ProdutoResult.VariantNotFound();
            }

            try
            {
                if (ativa.Value)
                {
                    produto.AtivarVariante(varianteId);
                }
                else
                {
                    produto.DesativarVariante(varianteId, novoPadraoId);
                }

                return ProdutoResult.Success(produto);
            }
            catch (InvalidOperationException exception)
            {
                return ProdutoResult.Conflict(exception.Message);
            }
            catch (ArgumentException exception)
            {
                return Invalid(exception);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProdutoResult> AlterarPublicacaoAsync(
        Guid produtoId,
        bool? publicado,
        CancellationToken cancellationToken = default)
    {
        if (publicado is null)
        {
            return Invalid("publicado", "O estado de publicação é obrigatório.");
        }

        return await _repository.ExecutarMutacaoAsync(async (session, token) =>
        {
            var produto = await session.BuscarProdutoAsync(produtoId, token).ConfigureAwait(false);
            if (produto is null)
            {
                return ProdutoResult.ProductNotFound();
            }

            try
            {
                if (publicado.Value)
                {
                    produto.Publicar();
                }
                else
                {
                    produto.Despublicar();
                }

                return ProdutoResult.Success(produto);
            }
            catch (InvalidOperationException exception)
            {
                return produto.Categoria.Ativa
                    ? ProdutoResult.Conflict(exception.Message)
                    : ProdutoResult.CategoryInactive();
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private static ProdutoResult? CriarVariantes(
        ProdutoCadastroInput input,
        out List<ProdutoVariante> variantes)
    {
        variantes = [];
        if (input.VariantePadrao is null)
        {
            return Invalid("variantePadrao", "A variante padrão é obrigatória.");
        }

        try
        {
            variantes.Add(CriarVariante(input.VariantePadrao, padrao: true));
            foreach (var varianteInput in input.Variantes ?? [])
            {
                if (varianteInput is null)
                {
                    return Invalid("variantes", "As variantes não podem conter valores nulos.");
                }

                variantes.Add(CriarVariante(varianteInput, padrao: false));
            }

            return null;
        }
        catch (ArgumentException exception)
        {
            return Invalid(exception);
        }
    }

    private static ProdutoVariante CriarVariante(ProdutoVarianteInput input, bool padrao) =>
        ProdutoVariante.Criar(
            input.Identificacao,
            input.UnidadeVenda,
            input.PrecoVenda,
            ativa: true,
            padrao);

    private static ProdutoResult Invalid(string campo, string mensagem) =>
        ProdutoResult.ValidationFailed(new ProdutoValidationError(campo, mensagem));

    private static ProdutoResult Invalid(ArgumentException exception)
    {
        var campo = exception.ParamName switch
        {
            "nome" => "nome",
            "descricao" => "descricao",
            "categoria" => "categoriaId",
            "identificacao" => "identificacao",
            "unidadeVenda" => "unidadeVenda",
            "precoVenda" => "precoVenda",
            "variantes" or "variante" => "variantes",
            "ativa" => "ativa",
            _ => "produto"
        };
        return Invalid(campo, exception.Message);
    }
}
