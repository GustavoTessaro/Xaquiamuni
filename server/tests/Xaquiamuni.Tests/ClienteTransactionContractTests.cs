using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Reflection;
using Xaquiamuni.Aplicacao.Cadastro;
using Xaquiamuni.Infraestrutura.Persistencia;

namespace Xaquiamuni.Tests;

public class ClienteTransactionContractTests
{
    [Fact]
    public void TransactionContract_ExposesAsyncSaveCommitRollbackAndDisposeWithoutEfTypes()
    {
        var methods = typeof(IClienteTransaction).GetMethods();

        Assert.Equal(3, methods.Length);
        Assert.Contains(methods, method => method.Name == nameof(IClienteTransaction.SaveChangesAsync));
        Assert.Contains(methods, method => method.Name == nameof(IClienteTransaction.CommitAsync));
        Assert.Contains(methods, method => method.Name == nameof(IClienteTransaction.RollbackAsync));
        Assert.True(typeof(IAsyncDisposable).IsAssignableFrom(typeof(IClienteTransaction)));
        Assert.All(methods, method =>
        {
            Assert.DoesNotContain("EntityFrameworkCore", method.ReturnType.Namespace ?? string.Empty);
            Assert.All(method.GetParameters(), parameter =>
                Assert.DoesNotContain("EntityFrameworkCore", parameter.ParameterType.Namespace ?? string.Empty));
        });
        Assert.Equal(typeof(Task<int>), typeof(IClienteTransaction)
            .GetMethod(nameof(IClienteTransaction.SaveChangesAsync))!.ReturnType);
        Assert.Equal(typeof(Task), typeof(IClienteTransaction)
            .GetMethod(nameof(IClienteTransaction.CommitAsync))!.ReturnType);
        Assert.Equal(typeof(Task), typeof(IClienteTransaction)
            .GetMethod(nameof(IClienteTransaction.RollbackAsync))!.ReturnType);
    }

    [Fact]
    public void TransactionFactory_ContractBeginsAsynchronouslyWithoutExposingEfTypes()
    {
        var method = Assert.Single(typeof(IClienteTransactionFactory).GetMethods());

        Assert.Equal(nameof(IClienteTransactionFactory.BeginTransactionAsync), method.Name);
        Assert.Equal(typeof(Task<IClienteTransaction>), method.ReturnType);
        Assert.All(method.GetParameters(), parameter =>
            Assert.DoesNotContain("EntityFrameworkCore", parameter.ParameterType.Namespace ?? string.Empty));
    }

    [Fact]
    public async Task DisposeAsync_RollsBackUncommittedTransaction()
    {
        var (context, transaction, spy) = CreateTransaction();
        await using (context)
        await using (transaction)
        {
        }

        Assert.Equal(1, spy.RollbackCalls);
        Assert.Equal(1, spy.DisposeCalls);
    }

    [Fact]
    public async Task DisposeAsync_DoesNotRollbackCommittedTransaction()
    {
        var (context, transaction, spy) = CreateTransaction();
        await using (context)
        await using (transaction)
        {
            await transaction.CommitAsync();
        }

        Assert.Equal(0, spy.RollbackCalls);
        Assert.Equal(1, spy.DisposeCalls);
    }

    private static (XaquiamuniDbContext Context, EfClienteTransaction Transaction, TransactionSpy Spy)
        CreateTransaction()
    {
        var options = new DbContextOptionsBuilder<XaquiamuniDbContext>()
            .UseSqlServer("Server=localhost;Database=XaquiamuniTransactionContractTest")
            .Options;
        var context = new XaquiamuniDbContext(options);
        var databaseTransaction = DispatchProxy.Create<IDbContextTransaction, TransactionSpy>();
        var spy = (TransactionSpy)(object)databaseTransaction;
        return (context, new EfClienteTransaction(context, databaseTransaction), spy);
    }

    public class TransactionSpy : DispatchProxy
    {
        public int RollbackCalls { get; private set; }
        public int DisposeCalls { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case nameof(IDbContextTransaction.RollbackAsync):
                    RollbackCalls++;
                    return Task.CompletedTask;
                case nameof(IDbContextTransaction.CommitAsync):
                    return Task.CompletedTask;
                case nameof(IAsyncDisposable.DisposeAsync):
                    DisposeCalls++;
                    return ValueTask.CompletedTask;
                default:
                    throw new NotSupportedException();
            }
        }
    }
}
