import { InicioProducao } from '../../core/ordens-producao.service';

export type TentativaProducao =
  | { tipo: 'inicio'; chave: string; corpo: InicioProducao }
  | { tipo: 'cancelamento'; chave: string; ordemId: string };

const uuid = /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i;
const idValido = (id: unknown): id is string => typeof id === 'string' && uuid.test(id);

export function quantidadePlanejadaValida(quantidade: number | null): quantidade is number {
  return (
    quantidade !== null &&
    Number.isInteger(quantidade) &&
    quantidade > 0 &&
    quantidade <= 2147483647
  );
}

export function lerTentativaProducao(serializado: string | null): TentativaProducao | null {
  if (!serializado) return null;
  try {
    const dado = JSON.parse(serializado);
    if (!idValido(dado?.chave)) return null;
    if (dado.tipo === 'cancelamento' && idValido(dado.ordemId))
      return { tipo: dado.tipo, chave: dado.chave, ordemId: dado.ordemId };
    const corpo = dado.corpo;
    if (
      dado.tipo === 'inicio' &&
      idValido(corpo?.receitaId) &&
      idValido(corpo.receitaVersaoId) &&
      quantidadePlanejadaValida(corpo.quantidade)
    )
      return {
        tipo: dado.tipo,
        chave: dado.chave,
        corpo: {
          receitaId: corpo.receitaId,
          receitaVersaoId: corpo.receitaVersaoId,
          quantidade: corpo.quantidade,
        },
      };
  } catch {
    return null;
  }
  return null;
}
