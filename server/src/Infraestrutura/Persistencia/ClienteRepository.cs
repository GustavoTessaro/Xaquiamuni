using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Cadastro;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed class ClienteRepository : IClienteRepository, IConsultarEnderecoPadrao
{
    private readonly XaquiamuniDbContext _dbContext;

    public ClienteRepository(XaquiamuniDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<EnderecoPadraoCliente?> BuscarAsync(Guid clienteId, CancellationToken cancellationToken)
    {
        var salvo = await _dbContext.ClienteEnderecos.AsNoTracking()
            .SingleOrDefaultAsync(endereco => endereco.ClienteUserId == clienteId && endereco.IsDefault, cancellationToken);
        if (salvo is null) return null;

        try
        {
            var endereco = ClienteEndereco.Criar(salvo.Id, clienteId, salvo.Cep, salvo.Uf, salvo.Cidade,
                salvo.Bairro, salvo.Logradouro, salvo.Numero, salvo.Complemento, isDefault: true);
            return new(endereco.Cep, endereco.Uf, endereco.Cidade, endereco.Bairro, endereco.Logradouro,
                endereco.Numero, endereco.Complemento);
        }
        catch (ArgumentException)
        {
            return null;
        }
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
