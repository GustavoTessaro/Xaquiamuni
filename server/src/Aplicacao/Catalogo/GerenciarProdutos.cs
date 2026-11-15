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
                var produto = CriarProduto(input, categoria);
                var references = AplicarCaracteristicas(produto, input.Caracteristicas, creating: true);
                AplicarAssociacoesVariantes(
                    produto,
                    CriarVariantesInput(input),
                    references);
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

                var references = AplicarCaracteristicas(
                    produto,
                    input.Caracteristicas,
                    creating: false);
                if (input.Variantes is not null)
                {
                    var appliedVariants = AplicarVariantesEdicao(
                        produto,
                        input.Variantes,
                        references);
                    AplicarEstadosVariantes(produto, appliedVariants);
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

    public async Task<ProdutoResult> AdicionarVarianteAsync(
        Guid produtoId,
        ProdutoVarianteInput? input,
        CancellationToken cancellationToken = default)
    {
        if (input is null)
        {
            return Invalid("variante", "Os dados da variante são obrigatórios.");
        }

        if (input.Padrao == true)
        {
            return Invalid("variantePadrao", "Use a operação de definição de variante padrão após o cadastro.");
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
                var references = AplicarCaracteristicas(produto, null, creating: false);
                AplicarAssociacoesVariante(
                    produto,
                    variante,
                    input.Caracteristicas,
                    references);
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

        if (input.Id is not null && input.Id != varianteId)
        {
            return Invalid("varianteId", "O ID do corpo deve corresponder à variante da rota.");
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
                var variante = produto.Variantes.Single(item => item.Id == varianteId);
                produto.AtualizarVariante(
                    varianteId,
                    input.Identificacao ?? variante.Identificacao,
                    input.UnidadeVenda ?? variante.UnidadeVenda,
                    input.PrecoVenda ?? variante.PrecoVenda);
                var references = AplicarCaracteristicas(produto, null, creating: false);
                if (input.Caracteristicas is not null)
                {
                    var associations = ResolverAssociacoesVariante(
                        produto,
                        input.Caracteristicas,
                        references);
                    produto.SubstituirValoresCaracteristica(varianteId, []);
                    produto.SubstituirValoresCaracteristica(varianteId, associations);
                }

                if (input.AtualizarMedidas
                    || input.PesoLiquidoGramas is not null
                    || input.VolumeLiquidoMililitros is not null)
                {
                    variante.DefinirMedidasLiquidas(
                        input.PesoLiquidoGramas,
                        input.VolumeLiquidoMililitros);
                }

                AplicarEstadosVariantes(produto, [new VarianteAplicada(input, variante)]);
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

    private static Produto CriarProduto(ProdutoCadastroInput input, Categoria categoria)
    {
        var variantePadrao = input.VariantePadrao
            ?? throw new ArgumentException("A variante padrão é obrigatória.", "variantePadrao");

        if (input.Simples == true)
        {
            if (input.Variantes is { Count: > 0 }
                || input.Caracteristicas is { Count: > 0 })
            {
                throw new ArgumentException(
                    "Um produto simples não aceita variantes adicionais nem características.",
                    "simples");
            }

            if (variantePadrao.Id is not null)
            {
                throw new ArgumentException(
                    "IDs persistidos não podem ser informados no cadastro.",
                    "variantes.id");
            }

            if (variantePadrao.Ativa == false)
            {
                throw new ArgumentException("A variante padrão deve estar ativa.", "ativa");
            }

            if (variantePadrao.PrecoVenda is null)
            {
                throw new ArgumentException("O preço de venda é obrigatório.", "precoVenda");
            }

            return Produto.CriarSimples(
                input.Nome,
                input.Descricao,
                categoria,
                variantePadrao.UnidadeVenda,
                variantePadrao.PrecoVenda.Value,
                variantePadrao.PesoLiquidoGramas,
                variantePadrao.VolumeLiquidoMililitros);
        }

        var variantes = new List<ProdutoVariante>
        {
            CriarVariante(variantePadrao, padrao: true)
        };
        foreach (var varianteInput in input.Variantes ?? [])
        {
            if (varianteInput is null)
            {
                throw new ArgumentException("As variantes não podem conter valores nulos.", "variantes");
            }

            if (varianteInput.Padrao == true)
            {
                throw new ArgumentException(
                    "A variante padrão deve ser informada somente em variantePadrao.",
                    "variantes");
            }

            variantes.Add(CriarVariante(varianteInput, padrao: false));
        }

        return Produto.Criar(input.Nome, input.Descricao, categoria, variantes);
    }

    private static IReadOnlyList<ProdutoVarianteInput?> CriarVariantesInput(ProdutoCadastroInput input)
    {
        if (input.Simples == true)
        {
            return [input.VariantePadrao];
        }

        return [input.VariantePadrao, .. input.Variantes ?? []];
    }

    private static ProdutoVariante CriarVariante(ProdutoVarianteInput input, bool padrao)
    {
        if (input.Id is not null)
        {
            throw new ArgumentException(
                "IDs de variantes existentes não podem ser informados no cadastro.",
                "variantes.id");
        }

        if (input.PrecoVenda is null)
        {
            throw new ArgumentException("O preço de venda é obrigatório.", "precoVenda");
        }

        if (padrao && input.Ativa == false)
        {
            throw new ArgumentException("A variante padrão deve estar ativa.", "ativa");
        }

        return ProdutoVariante.Criar(
            input.Identificacao,
            input.UnidadeVenda,
            input.PrecoVenda.Value,
            ativa: padrao || (input.Ativa ?? true),
            padrao,
            input.PesoLiquidoGramas,
            input.VolumeLiquidoMililitros);
    }

    private static ReferenciasCaracteristicas AplicarCaracteristicas(
        Produto produto,
        IReadOnlyList<ProdutoCaracteristicaInput?>? inputs,
        bool creating)
    {
        var characteristicReferences = new Dictionary<string, ProdutoCaracteristica>(StringComparer.Ordinal);
        var valueReferences = new Dictionary<string, ProdutoCaracteristicaValor>(StringComparer.Ordinal);
        if (inputs is null)
        {
            return new ReferenciasCaracteristicas(characteristicReferences, valueReferences);
        }

        var seenIds = new HashSet<Guid>();
        foreach (var input in inputs)
        {
            if (input is null)
            {
                throw new ArgumentException(
                    "As características não podem conter valores nulos.",
                    "caracteristicas");
            }

            ProdutoCaracteristica characteristic;
            if (input.Id is { } id)
            {
                if (creating || id == Guid.Empty || !seenIds.Add(id))
                {
                    throw new ArgumentException(
                        "A referência de característica é inválida ou duplicada.",
                        "caracteristicas.id");
                }

                characteristic = produto.Caracteristicas.SingleOrDefault(item => item.Id == id)
                    ?? throw new ArgumentException(
                        "A característica informada não pertence a este produto.",
                        "caracteristicas.id");

                if (input.Remover)
                {
                    if (input.Valores is not null)
                    {
                        throw new ArgumentException(
                            "Uma característica removida não pode incluir valores.",
                            "caracteristicas.valores");
                    }

                    produto.RemoverCaracteristica(id);
                    continue;
                }

                var name = input.Nome ?? characteristic.Nome;
                var physicalType = ParseTipoMedidaFisica(input.TipoMedidaFisica)
                    ?? characteristic.TipoMedidaFisica;
                produto.AtualizarCaracteristica(id, name, physicalType);
                characteristic = produto.Caracteristicas.Single(item => item.Id == id);
            }
            else
            {
                if (creating && input.Remover)
                {
                    throw new ArgumentException(
                        "Não é possível remover uma característica durante o cadastro.",
                        "caracteristicas");
                }

                if (input.Remover)
                {
                    throw new ArgumentException(
                        "A remoção exige o ID persistido da característica.",
                        "caracteristicas.id");
                }

                characteristic = produto.AdicionarCaracteristica(
                    input.Nome,
                    ParseTipoMedidaFisica(input.TipoMedidaFisica));
            }

            AddReference(characteristicReferences, input.Referencia, characteristic, "caracteristicas.referencia");
            if (input.Valores is null)
            {
                continue;
            }

            var seenValueIds = new HashSet<Guid>();
            foreach (var valueInput in input.Valores)
            {
                if (valueInput is null)
                {
                    throw new ArgumentException(
                        "Os valores da característica não podem conter valores nulos.",
                        "caracteristicas.valores");
                }

                ProdutoCaracteristicaValor value;
                if (valueInput.Id is { } valueId)
                {
                    if (creating || valueId == Guid.Empty || !seenValueIds.Add(valueId))
                    {
                        throw new ArgumentException(
                            "A referência de valor é inválida ou duplicada.",
                            "caracteristicas.valores.id");
                    }

                    value = characteristic.Valores.SingleOrDefault(item => item.Id == valueId)
                        ?? throw new ArgumentException(
                            "O valor informado não pertence à característica.",
                            "caracteristicas.valores.id");
                    if (valueInput.Remover)
                    {
                        produto.RemoverValorCaracteristica(characteristic.Id, valueId);
                        continue;
                    }

                    characteristic.AtualizarValor(
                        valueId,
                        valueInput.Nome,
                        valueInput.ValorMedidaCanonico);
                    value = characteristic.Valores.Single(item => item.Id == valueId);
                }
                else
                {
                    if (valueInput.Remover)
                    {
                        throw new ArgumentException(
                            "A remoção exige o ID persistido do valor.",
                            "caracteristicas.valores.id");
                    }

                    value = AdicionarValorCaracteristica(characteristic, valueInput);
                }

                AddReference(
                    valueReferences,
                    valueInput.Referencia,
                    value,
                    "caracteristicas.valores.referencia");
            }
        }

        return new ReferenciasCaracteristicas(characteristicReferences, valueReferences);
    }

    private static ProdutoCaracteristicaValor AdicionarValorCaracteristica(
        ProdutoCaracteristica characteristic,
        ProdutoCaracteristicaValorInput input)
    {
        if (characteristic.TipoMedidaFisica is null)
        {
            if (input.ValorMedidaCanonico is not null)
            {
                throw new ArgumentException(
                    "Uma característica genérica não aceita valor numérico físico.",
                    "caracteristicas.valores.valorMedidaCanonico");
            }

            return characteristic.AdicionarValor(input.Nome);
        }

        if (input.Nome is not null || input.ValorMedidaCanonico is null)
        {
            throw new ArgumentException(
                "Uma característica física exige somente valorMedidaCanonico.",
                "caracteristicas.valores.valorMedidaCanonico");
        }

        var unit = characteristic.TipoMedidaFisica == TipoMedidaFisica.PesoLiquido ? "G" : "ML";
        return characteristic.AdicionarValorMedida(input.ValorMedidaCanonico.Value, unit);
    }

    private static TipoMedidaFisica? ParseTipoMedidaFisica(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim() switch
        {
            nameof(TipoMedidaFisica.PesoLiquido) => TipoMedidaFisica.PesoLiquido,
            nameof(TipoMedidaFisica.VolumeLiquido) => TipoMedidaFisica.VolumeLiquido,
            _ => throw new ArgumentException(
                "O tipo de medida física deve ser PesoLiquido ou VolumeLiquido.",
                "caracteristicas.tipoMedidaFisica")
        };
    }

    private static void AddReference<T>(
        IDictionary<string, T> references,
        string? reference,
        T value,
        string field)
    {
        if (reference is null)
        {
            return;
        }

        var normalized = reference.Trim();
        if (normalized.Length == 0 || !references.TryAdd(normalized, value))
        {
            throw new ArgumentException(
                "A referência temporária está vazia ou duplicada.",
                field);
        }
    }

    private static void AplicarAssociacoesVariantes(
        Produto produto,
        IReadOnlyList<ProdutoVarianteInput?> variantInputs,
        ReferenciasCaracteristicas references)
    {
        var variants = produto.Variantes.ToArray();
        if (variants.Length != variantInputs.Count)
        {
            throw new InvalidOperationException("As variantes recebidas não correspondem ao produto criado.");
        }

        for (var index = 0; index < variantInputs.Count; index++)
        {
            var input = variantInputs[index];
            if (input?.Caracteristicas is null)
            {
                continue;
            }

            AplicarAssociacoesVariante(
                produto,
                variants[index],
                input.Caracteristicas,
                references);
        }
    }

    private static void AplicarAssociacoesVariante(
        Produto produto,
        ProdutoVariante variant,
        IReadOnlyList<ProdutoVarianteCaracteristicaInput?>? inputs,
        ReferenciasCaracteristicas references)
    {
        if (inputs is null)
        {
            return;
        }

        produto.SubstituirValoresCaracteristica(
            variant.Id,
            ResolverAssociacoesVariante(produto, inputs, references));
    }

    private static IReadOnlyList<(Guid CaracteristicaId, Guid ValorId)> ResolverAssociacoesVariante(
        Produto produto,
        IReadOnlyList<ProdutoVarianteCaracteristicaInput?> inputs,
        ReferenciasCaracteristicas references)
    {
        var values = new List<(Guid CaracteristicaId, Guid ValorId)>();
        var characteristicIds = new HashSet<Guid>();
        foreach (var input in inputs)
        {
            if (input is null)
            {
                throw new ArgumentException(
                    "As associações de características não podem conter valores nulos.",
                    "variantes.caracteristicas");
            }

            var characteristic = ResolverCaracteristica(produto, input, references);
            if (!characteristicIds.Add(characteristic.Id))
            {
                throw new ArgumentException(
                    "Uma variante pode receber somente um valor por característica.",
                    "variantes.caracteristicas.caracteristicaId");
            }

            var value = ResolverValor(characteristic, input, references);
            values.Add((characteristic.Id, value.Id));
        }

        return values;
    }

    private static ProdutoCaracteristica ResolverCaracteristica(
        Produto produto,
        ProdutoVarianteCaracteristicaInput input,
        ReferenciasCaracteristicas references)
    {
        if ((input.CaracteristicaId is null) == string.IsNullOrWhiteSpace(input.ReferenciaCaracteristica))
        {
            throw new ArgumentException(
                "Informe exatamente um ID ou uma referência de característica.",
                "variantes.caracteristicas.caracteristicaId");
        }

        if (input.CaracteristicaId is { } id)
        {
            if (id == Guid.Empty)
            {
                throw new ArgumentException("O ID da característica é inválido.", "variantes.caracteristicas");
            }

            return produto.Caracteristicas.SingleOrDefault(item => item.Id == id)
                ?? throw new ArgumentException(
                    "A característica não pertence a este produto.",
                    "variantes.caracteristicas.caracteristicaId");
        }

        var reference = input.ReferenciaCaracteristica!.Trim();
        return references.Caracteristicas.TryGetValue(reference, out var characteristic)
            ? characteristic
            : throw new ArgumentException(
                "A referência de característica não foi definida nesta operação.",
                "variantes.caracteristicas.referenciaCaracteristica");
    }

    private static ProdutoCaracteristicaValor ResolverValor(
        ProdutoCaracteristica characteristic,
        ProdutoVarianteCaracteristicaInput input,
        ReferenciasCaracteristicas references)
    {
        if ((input.ValorId is null) == string.IsNullOrWhiteSpace(input.ReferenciaValor))
        {
            throw new ArgumentException(
                "Informe exatamente um ID ou uma referência de valor.",
                "variantes.caracteristicas.valorId");
        }

        if (input.ValorId is { } id)
        {
            if (id == Guid.Empty)
            {
                throw new ArgumentException("O ID do valor é inválido.", "variantes.caracteristicas.valorId");
            }

            return characteristic.Valores.SingleOrDefault(item => item.Id == id)
                ?? throw new ArgumentException(
                    "O valor não pertence à característica informada.",
                    "variantes.caracteristicas.valorId");
        }

        var reference = input.ReferenciaValor!.Trim();
        if (!references.Valores.TryGetValue(reference, out var value)
            || value.ProdutoCaracteristicaId != characteristic.Id)
        {
            throw new ArgumentException(
                "A referência do valor não pertence à característica informada.",
                "variantes.caracteristicas.referenciaValor");
        }

        return value;
    }

    private static IReadOnlyList<VarianteAplicada> AplicarVariantesEdicao(
        Produto produto,
        IReadOnlyList<ProdutoVarianteInput?> inputs,
        ReferenciasCaracteristicas references)
    {
        var result = new List<VarianteAplicada>();
        var seenIds = new HashSet<Guid>();
        foreach (var input in inputs)
        {
            if (input is null)
            {
                throw new ArgumentException("As variantes não podem conter valores nulos.", "variantes");
            }

            ProdutoVariante variant;
            if (input.Id is null)
            {
                if (input.Padrao == true)
                {
                    throw new ArgumentException(
                        "Uma variante nova não pode ser marcada como padrão neste campo.",
                        "variantes.padrao");
                }

                variant = CriarVariante(input, padrao: false);
                produto.AdicionarVariante(variant);
            }
            else
            {
                var id = input.Id.Value;
                if (id == Guid.Empty || !seenIds.Add(id))
                {
                    throw new ArgumentException("O ID da variante é inválido ou duplicado.", "variantes.id");
                }

                variant = produto.Variantes.SingleOrDefault(item => item.Id == id)
                    ?? throw new ArgumentException(
                        "A variante não pertence a este produto.",
                        "variantes.id");
                produto.AtualizarVariante(
                    id,
                    input.Identificacao ?? variant.Identificacao,
                    input.UnidadeVenda ?? variant.UnidadeVenda,
                    input.PrecoVenda ?? variant.PrecoVenda);
            }

            result.Add(new VarianteAplicada(input, variant));
        }

        var resolvedAssociations = result
            .Select(item => new
            {
                Item = item,
                Values = item.Input.Caracteristicas is null
                    ? null
                    : ResolverAssociacoesVariante(produto, item.Input.Caracteristicas, references)
            })
            .ToArray();

        foreach (var association in resolvedAssociations.Where(item => item.Values is not null))
        {
            produto.SubstituirValoresCaracteristica(association.Item.Variante.Id, []);
        }

        foreach (var association in resolvedAssociations)
        {
            if (association.Values is not null)
            {
                produto.SubstituirValoresCaracteristica(
                    association.Item.Variante.Id,
                    association.Values);
            }

            if (association.Item.Input.AtualizarMedidas
                || association.Item.Input.PesoLiquidoGramas is not null
                || association.Item.Input.VolumeLiquidoMililitros is not null)
            {
                association.Item.Variante.DefinirMedidasLiquidas(
                    association.Item.Input.PesoLiquidoGramas,
                    association.Item.Input.VolumeLiquidoMililitros);
            }
        }

        return result;
    }

    private static void AplicarEstadosVariantes(
        Produto produto,
        IReadOnlyList<VarianteAplicada> appliedVariants)
    {
        var requestedDefaults = appliedVariants.Where(item => item.Input.Padrao == true).ToArray();
        if (requestedDefaults.Length > 1)
        {
            throw new ArgumentException(
                "Somente uma variante pode ser informada como padrão.",
                "variantes.padrao");
        }

        foreach (var item in appliedVariants.Where(item => item.Input.Ativa == true))
        {
            produto.AtivarVariante(item.Variante.Id);
        }

        var newDefault = requestedDefaults.SingleOrDefault()?.Variante;
        if (newDefault is null
            && appliedVariants.Any(item => item.Input.Padrao == false && item.Variante.Padrao))
        {
            throw new ArgumentException(
                "Informe outra variante como padrão na mesma edição.",
                "variantes.padrao");
        }

        if (newDefault is not null)
        {
            produto.DefinirVariantePadrao(newDefault.Id);
        }

        foreach (var item in appliedVariants.Where(item => item.Input.Ativa == false))
        {
            if (!item.Variante.Ativa)
            {
                continue;
            }

            var replacementId = item.Variante.Padrao
                ? item.Input.NovoPadraoId ?? newDefault?.Id
                : item.Input.NovoPadraoId == newDefault?.Id
                    ? null
                    : item.Input.NovoPadraoId;
            produto.DesativarVariante(item.Variante.Id, replacementId);
        }
    }

    private sealed record ReferenciasCaracteristicas(
        IReadOnlyDictionary<string, ProdutoCaracteristica> Caracteristicas,
        IReadOnlyDictionary<string, ProdutoCaracteristicaValor> Valores);

    private sealed record VarianteAplicada(ProdutoVarianteInput Input, ProdutoVariante Variante);

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
            "pesoLiquidoGramas" => "pesoLiquidoGramas",
            "volumeLiquidoMililitros" => "volumeLiquidoMililitros",
            "valorMedidaCanonico" => "valorMedidaCanonico",
            "variantes" or "variante" => "variantes",
            "ativa" => "ativa",
            "caracteristicas" or "caracteristicas.id" or "caracteristicas.referencia"
                or "caracteristicas.tipoMedidaFisica" => "caracteristicas",
            "caracteristicas.valores" or "caracteristicas.valores.id"
                or "caracteristicas.valores.referencia"
                or "caracteristicas.valores.valorMedidaCanonico" => "caracteristicas.valores",
            "variantes.id" => "variantes",
            "variantes.padrao" => "variantes.padrao",
            "variantes.caracteristicas" or "variantes.caracteristicas.caracteristicaId"
                or "variantes.caracteristicas.valorId"
                or "variantes.caracteristicas.referenciaCaracteristica"
                or "variantes.caracteristicas.referenciaValor" => "variantes.caracteristicas",
            "simples" => "simples",
            _ => exception.ParamName ?? "produto"
        };
        return Invalid(campo, exception.Message);
    }
}
