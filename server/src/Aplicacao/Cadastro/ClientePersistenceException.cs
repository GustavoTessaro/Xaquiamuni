namespace Xaquiamuni.Aplicacao.Cadastro;

public class ClientePersistenceException : Exception
{
    public ClientePersistenceException()
        : base("Não foi possível persistir os dados do cliente.")
    {
    }
}

public sealed class ClienteCpfDuplicadoException : ClientePersistenceException
{
    public ClienteCpfDuplicadoException()
        : base()
    {
    }
}
