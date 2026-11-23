using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Aplicacao.Catalogo;

public sealed class GerenciarCategorias : IGerenciarCategorias
{
    private readonly ICategoriaRepository _repository;

    public GerenciarCategorias(ICategoriaRepository repository)
    {
        _repository = repository;
    }

    public async Task<CategoriaResult> CadastrarAsync(
        CategoriaInput? input,
        CancellationToken cancellationToken = default)
    {
        if (input is null)
        {
            return Invalid("categoria", "Os dados da categoria são obrigatórios.");
        }

        Categoria categoria;
        try
        {
            categoria = Categoria.Criar(input.Nome, input.Descricao, input.OrdemExibicao);
        }
        catch (ArgumentException exception)
        {
            return Invalid(exception);
        }

        if (await _repository.ExisteNomeNormalizadoAsync(
                categoria.NomeNormalizado,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false))
        {
            return CategoriaResult.NameAlreadyExists();
        }

        return await _repository.AdicionarAsync(categoria, cancellationToken).ConfigureAwait(false)
            ? CategoriaResult.Success(categoria)
            : CategoriaResult.NameAlreadyExists();
    }

    public Task<Categoria?> ConsultarAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        _repository.BuscarPorIdAsync(id, cancellationToken);

    public Task<IReadOnlyList<Categoria>> ListarAsync(
        CancellationToken cancellationToken = default) =>
        _repository.ListarAsync(cancellationToken);

    public async Task<CategoriaResult> EditarAsync(
        Guid id,
        CategoriaInput? input,
        CancellationToken cancellationToken = default)
    {
        if (input is null)
        {
            return Invalid("categoria", "Os dados da categoria são obrigatórios.");
        }

        var categoria = await _repository.BuscarPorIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (categoria is null)
        {
            return CategoriaResult.NotFound();
        }

        if (input.VersaoEsperada is { } versao && categoria.Versao != versao)
            return CategoriaResult.VersionConflict();

        Categoria candidate;
        try
        {
            candidate = Categoria.Criar(input.Nome, input.Descricao, input.OrdemExibicao);
        }
        catch (ArgumentException exception)
        {
            return Invalid(exception);
        }

        if (await _repository.ExisteNomeNormalizadoAsync(
                candidate.NomeNormalizado,
                id,
                cancellationToken)
            .ConfigureAwait(false))
        {
            return CategoriaResult.NameAlreadyExists();
        }

        categoria.AtualizarDados(input.Nome, input.Descricao, input.OrdemExibicao);
        return await AtualizarAsync(categoria, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CategoriaResult> AlterarStatusAsync(
        Guid id,
        bool? ativa,
        CancellationToken cancellationToken = default)
    {
        if (ativa is null)
        {
            return Invalid("ativa", "O status da categoria é obrigatório.");
        }

        var categoria = await _repository.BuscarPorIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (categoria is null)
        {
            return CategoriaResult.NotFound();
        }

        categoria.DefinirAtiva(ativa.Value);
        return await AtualizarAsync(categoria, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CategoriaResult> AtualizarAsync(Categoria categoria, CancellationToken cancellationToken)
    {
        try
        {
            return await _repository.AtualizarAsync(categoria, cancellationToken).ConfigureAwait(false)
                ? CategoriaResult.Success(categoria) : CategoriaResult.NameAlreadyExists();
        }
        catch (CategoriaVersaoException)
        {
            return CategoriaResult.VersionConflict();
        }
    }

    private static CategoriaResult Invalid(string campo, string mensagem) =>
        CategoriaResult.ValidationFailed(new CategoriaValidationError(campo, mensagem));

    private static CategoriaResult Invalid(ArgumentException exception)
    {
        var campo = exception.ParamName switch
        {
            "nome" => "nome",
            "descricao" => "descricao",
            "ordemExibicao" => "ordemExibicao",
            _ => "categoria"
        };
        return Invalid(campo, exception.Message);
    }
}
