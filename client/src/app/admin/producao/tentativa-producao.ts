import { ConcluirProducao, InicioProducao } from '../../core/ordens-producao.service';

export type TentativaProducao =
  | { tipo: 'inicio'; chave: string; corpo: InicioProducao }
  | { tipo: 'cancelamento'; chave: string; ordemId: string }
  | { tipo: 'conclusao'; chave: string; ordemId: string; corpo: ConcluirProducao };

const uuid = /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i;
const idValido = (id: unknown): id is string => typeof id === 'string' && uuid.test(id);
const dataValida = (data: unknown) =>
  data === null || (typeof data === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(data));

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
      dado.tipo === 'conclusao' &&
      idValido(dado.ordemId) &&
      idValido(corpo?.versao) &&
      quantidadePlanejadaValida(corpo.quantidadeProduzida) &&
      dataValida(corpo.dataFabricacao) &&
      dataValida(corpo.dataValidade) &&
      Array.isArray(corpo.consumos) &&
      corpo.consumos.length >= 1 &&
      corpo.consumos.length <= 100 &&
      corpo.consumos.every((item: unknown) => {
        const consumo = item as ConcluirProducao['consumos'][number] | null;
        return (
          consumo &&
          idValido(consumo.insumoId) &&
          ['Grama', 'Mililitro', 'Unidade'].includes(consumo.unidadeBase) &&
          typeof consumo.quantidade === 'string' &&
          /^\d{1,12}(\.\d{1,6})?$/.test(consumo.quantidade)
        );
      })
    )
      return { tipo: dado.tipo, chave: dado.chave, ordemId: dado.ordemId, corpo };
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
