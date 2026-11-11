using Xaquiamuni.Dominio.Valores;

namespace Xaquiamuni.Aplicacao.Seguranca;

public interface ICpfProtectionService
{
    CpfProtegido Protect(Cpf cpf);

    Cpf RecoverForAuthorizedUse(CpfProtegido protectedCpf);

    string CreateFingerprintForLookup(Cpf cpf);
}
