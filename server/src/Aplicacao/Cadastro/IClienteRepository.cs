using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Aplicacao.Cadastro;

/// <summary>Rastreia entidades sem confirmar alterações; a gravação pertence ao caso de uso/transação.</summary>
public interface IClienteRepository
{
    void Adicionar(ClientePerfil perfil);

    void Adicionar(ClienteEndereco endereco);
}

public interface IConsultarEnderecoPadrao
{
    Task<EnderecoPadraoCliente?> BuscarAsync(Guid clienteId, CancellationToken cancellationToken);
}

public interface IContaCliente
{
    Task<PerfilClienteVersionado?> BuscarPerfilAsync(Guid clienteId, CancellationToken cancellationToken);
    Task<AtualizacaoContaCliente> AtualizarPerfilAsync(Guid clienteId, string versao, DadosPessoaisCliente dados, CancellationToken ct);
    Task<AtualizacaoContaCliente> AtualizarEnderecoAsync(Guid clienteId, string versao, EnderecoInicialInput dados, CancellationToken ct);
}

public sealed record DadosPessoaisCliente(string? NomeCompleto, string? Telefone);

public sealed record PerfilClienteVersionado(ResumoPerfilCliente Dados, string VersaoPerfil, string VersaoEndereco);

public sealed record AtualizacaoContaCliente(PerfilClienteVersionado? Perfil, bool Conflito = false,
    bool PerfilIncompleto = false);

public sealed record ResumoPerfilCliente(string? NomeCompleto, string? Email, string? Telefone,
    EnderecoPadraoCliente? Endereco);

public sealed record EnderecoPadraoCliente(string Cep, string Uf, string Cidade, string Bairro,
    string Logradouro, string Numero, string? Complemento);
