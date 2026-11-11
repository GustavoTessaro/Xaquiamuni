using Xaquiamuni.Aplicacao.Cadastro;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed class ClienteRepository : IClienteRepository
{
    private readonly XaquiamuniDbContext _dbContext;

    public ClienteRepository(XaquiamuniDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void Adicionar(ClientePerfil perfil)
    {
        ArgumentNullException.ThrowIfNull(perfil);
        _dbContext.ClientePerfis.Add(perfil);
    }

    public void Adicionar(ClienteEndereco endereco)
    {
        ArgumentNullException.ThrowIfNull(endereco);
        _dbContext.ClienteEnderecos.Add(endereco);
    }
}
