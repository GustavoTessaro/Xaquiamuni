using Xaquiamuni.Dominio.Valores;

namespace Xaquiamuni.Aplicacao.Seguranca;

public interface ICpfProtectionService
{
    ProtectedCpf Protect(Cpf cpf);

    Cpf RecoverForAuthorizedUse(ProtectedCpf protectedCpf);

    string CreateFingerprintForLookup(Cpf cpf);
}
