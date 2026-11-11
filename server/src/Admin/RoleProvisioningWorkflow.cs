using Xaquiamuni.Infraestrutura.Identidade;

namespace Xaquiamuni.Admin;

public enum RoleProvisioningCommandStatus
{
    NotConfirmed,
    Completed
}

public sealed class RoleProvisioningWorkflow
{
    public const string ConfirmationText = "XaquiamuniDev";

    private readonly IRoleProvisioningPreflight _preflight;
    private readonly Func<CancellationToken, Task> _initializeRoles;

    public RoleProvisioningWorkflow(
        IRoleProvisioningPreflight preflight,
        Func<CancellationToken, Task> initializeRoles)
    {
        _preflight = preflight;
        _initializeRoles = initializeRoles;
    }

    public async Task<RoleProvisioningCommandStatus> ExecuteAsync(
        string? confirmation,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(confirmation, ConfirmationText, StringComparison.Ordinal))
        {
            return RoleProvisioningCommandStatus.NotConfirmed;
        }

        await _preflight.ValidateAsync(cancellationToken).ConfigureAwait(false);
        await _initializeRoles(cancellationToken).ConfigureAwait(false);
        return RoleProvisioningCommandStatus.Completed;
    }

    public static IReadOnlyList<string> RolesToEnsure =>
    [
        XaquiamuniRoles.Administrador,
        XaquiamuniRoles.Funcionario,
        XaquiamuniRoles.Cliente
    ];
}
