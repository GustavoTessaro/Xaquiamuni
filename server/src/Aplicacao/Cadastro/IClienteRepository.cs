using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Aplicacao.Cadastro;

/// <summary>Rastreia entidades sem confirmar alterações; a gravação pertence ao caso de uso/transação.</summary>
public interface IClienteRepository
{
    void Adicionar(ClientePerfil perfil);

    void Adicionar(ClienteEndereco endereco);
}
