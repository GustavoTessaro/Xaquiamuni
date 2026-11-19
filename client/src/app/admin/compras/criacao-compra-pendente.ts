import { CompraInput } from '../../core/compras-materias-primas.service';
import { dataCivilValida } from './recebimento-pendente';

export interface CriacaoCompraPendente {
  chave: string;
  corpo: CompraInput;
  fornecedor: string;
  materiais: Record<string, string>;
  apresentacoes: Record<string, string>;
}

const uuid = /^[a-f\d]{8}(?:-[a-f\d]{4}){3}-[a-f\d]{12}$/i;
const quantidade = /^\d{1,12}(\.\d{1,6})?$/;
const dinheiro = /^\d{1,16}(\.\d{1,2})?$/;

export function lerCriacaoCompra(texto: string | null): CriacaoCompraPendente | null {
  if (!texto) return null;
  try {
    const dado = JSON.parse(texto) as CriacaoCompraPendente;
    const { chave, corpo, fornecedor, materiais, apresentacoes } = dado;
    const nomesValidos = (nomes: unknown) =>
      nomes !== null &&
      typeof nomes === 'object' &&
      !Array.isArray(nomes) &&
      Object.values(nomes).every((nome) => typeof nome === 'string');
    if (
      typeof chave !== 'string' ||
      !uuid.test(chave) ||
      typeof fornecedor !== 'string' ||
      !nomesValidos(materiais) ||
      !nomesValidos(apresentacoes) ||
      typeof corpo?.fornecedorId !== 'string' ||
      !uuid.test(corpo.fornecedorId) ||
      typeof corpo.dataCompra !== 'string' ||
      !dataCivilValida(corpo.dataCompra) ||
      corpo.versao !== null ||
      !Array.isArray(corpo.linhas) ||
      corpo.linhas.length < 1 ||
      corpo.linhas.length > 50
    )
      return null;

    const valida = corpo.linhas.every(
      (linha) =>
        linha &&
        (linha.kit === null || typeof linha.kit === 'string') &&
        typeof linha.quantidade === 'string' &&
        quantidade.test(linha.quantidade) &&
        (linha.valorTotal === null ||
          (typeof linha.valorTotal === 'string' && dinheiro.test(linha.valorTotal))) &&
        Array.isArray(linha.componentes) &&
        linha.componentes.length >= 1 &&
        linha.componentes.length <= 30 &&
        linha.componentes.every(
          (item) =>
            item &&
            typeof item.insumoId === 'string' &&
            uuid.test(item.insumoId) &&
            (item.apresentacaoId === null ||
              (typeof item.apresentacaoId === 'string' && uuid.test(item.apresentacaoId))) &&
            typeof item.quantidade === 'string' &&
            quantidade.test(item.quantidade) &&
            item.snapshotId === null,
        ),
    );
    return valida ? dado : null;
  } catch {
    return null;
  }
}
