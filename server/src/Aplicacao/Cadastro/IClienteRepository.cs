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

public sealed record EnderecoPadraoCliente(string Cep, string Uf, string Cidade, string Bairro,
    string Logradouro, string Numero, string? Complemento);
