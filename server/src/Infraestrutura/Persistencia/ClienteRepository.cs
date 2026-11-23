using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xaquiamuni.Aplicacao.Cadastro;
using Xaquiamuni.Dominio.Entidades;

namespace Xaquiamuni.Infraestrutura.Persistencia;

public sealed class ClienteRepository : IClienteRepository, IConsultarEnderecoPadrao, IContaCliente
{
    private readonly XaquiamuniDbContext _dbContext;

    public ClienteRepository(XaquiamuniDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PerfilClienteVersionado?> BuscarPerfilAsync(Guid clienteId, CancellationToken cancellationToken)
    {
        var conta = await _dbContext.Users.AsNoTracking()
            .Where(usuario => usuario.Id == clienteId)
            .Select(usuario => new
            {
                usuario.Email,
                usuario.PhoneNumber,
                NomeCompleto = _dbContext.ClientePerfis.Where(perfil => perfil.UserId == usuario.Id)
                    .Select(perfil => perfil.NomeCompleto).FirstOrDefault()
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (conta is null) return null;

        var salvo = await ConsultarEndereco(false, clienteId, cancellationToken);
        return new(new(conta.NomeCompleto, conta.Email, conta.PhoneNumber, Projetar(salvo)),
            Versao("perfil", clienteId, conta.NomeCompleto, conta.PhoneNumber),
            VersaoEndereco(clienteId, salvo));
    }

    public async Task<AtualizacaoContaCliente> AtualizarPerfilAsync(Guid clienteId, string versao,
        DadosPessoaisCliente dados, CancellationToken ct)
    {
        return await AtualizarAsync(clienteId, async () =>
        {
            var conta = await _dbContext.Users.SingleOrDefaultAsync(x => x.Id == clienteId, ct);
            if (conta is null) return new(null);
            var perfil = await _dbContext.ClientePerfis.SingleOrDefaultAsync(x => x.UserId == clienteId, ct);
            if (versao != Versao("perfil", clienteId, perfil?.NomeCompleto, conta.PhoneNumber))
                return new(null, Conflito: true);
            if (perfil is null) return new(null, PerfilIncompleto: true);

            perfil.AtualizarNomeCompleto(dados.NomeCompleto);
            conta.PhoneNumber = dados.Telefone!.Trim();
            if (_dbContext.Entry(conta).Property(x => x.PhoneNumber).OriginalValue != conta.PhoneNumber)
            {
                conta.PhoneNumberConfirmed = false;
                conta.ConcurrencyStamp = Guid.NewGuid().ToString();
            }
            return null;
        }, ct);
    }

    public async Task<AtualizacaoContaCliente> AtualizarEnderecoAsync(Guid clienteId, string versao,
        EnderecoInicialInput dados, CancellationToken ct)
    {
        return await AtualizarAsync(clienteId, async () =>
        {
            if (!await _dbContext.Users.AnyAsync(x => x.Id == clienteId, ct)) return new(null);
            var salvo = await ConsultarEndereco(true, clienteId, ct);
            if (versao != VersaoEndereco(clienteId, salvo)) return new(null, Conflito: true);

            if (salvo is null)
                Adicionar(ClienteEndereco.Criar(Guid.NewGuid(), clienteId, dados.Cep, dados.Uf, dados.Cidade,
                    dados.Bairro, dados.Logradouro, dados.Numero, dados.Complemento, isDefault: true));
            else
                salvo.AtualizarDados(dados.Cep, dados.Uf, dados.Cidade, dados.Bairro, dados.Logradouro,
                    dados.Numero, dados.Complemento);
            return null;
        }, ct);
    }

    private async Task<AtualizacaoContaCliente> AtualizarAsync(Guid clienteId,
        Func<Task<AtualizacaoContaCliente?>> alterar, CancellationToken ct)
    {
        await using var transacao = await _dbContext.Database.BeginTransactionAsync(ct);
        var recurso = $"customer-account:{clienteId:D}";
        try
        {
            await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                DECLARE @resultado int;
                EXEC @resultado = sys.sp_getapplock @Resource = {recurso}, @LockMode = 'Exclusive',
                    @LockOwner = 'Transaction', @LockTimeout = 10000;
                IF @resultado < 0 THROW 51000, 'Customer account lock unavailable.', 1;
                """, ct);

            if (await alterar() is { } rejeicao) return rejeicao;
            await _dbContext.SaveChangesAsync(ct);
            var perfil = await BuscarPerfilAsync(clienteId, ct);
            await transacao.CommitAsync(ct);
            return new(perfil);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(null, Conflito: true);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 51000)
        {
            return new(null, Conflito: true);
        }
    }

    public async Task<EnderecoPadraoCliente?> BuscarAsync(Guid clienteId, CancellationToken cancellationToken) =>
        Projetar(await ConsultarEndereco(false, clienteId, cancellationToken));

    private Task<ClienteEndereco?> ConsultarEndereco(bool rastrear, Guid clienteId, CancellationToken ct) =>
        (rastrear ? _dbContext.ClienteEnderecos.AsTracking() : _dbContext.ClienteEnderecos.AsNoTracking())
            .SingleOrDefaultAsync(endereco => endereco.ClienteUserId == clienteId && endereco.IsDefault, ct);

    private static string VersaoEndereco(Guid clienteId, ClienteEndereco? salvo) =>
        Versao("endereco", clienteId, salvo?.Id, salvo?.Cep, salvo?.Uf, salvo?.Cidade,
            salvo?.Bairro, salvo?.Logradouro, salvo?.Numero, salvo?.Complemento);

    private static string Versao(params object?[] estado) =>
        $"\"{Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(estado)))}\"";

    private static EnderecoPadraoCliente? Projetar(ClienteEndereco? salvo)
    {
        if (salvo is null) return null;

        try
        {
            var endereco = ClienteEndereco.Criar(salvo.Id, salvo.ClienteUserId, salvo.Cep, salvo.Uf, salvo.Cidade,
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
