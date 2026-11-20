import { ReceberCompraInput } from '../../core/compras-materias-primas.service';

export interface RecebimentoPendente {
  compraId: string;
  chave: string;
  corpo: ReceberCompraInput;
}

const uuid = /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i;

export function dataCivilValida(valor: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(valor) || valor.startsWith('0000')) return false;
  const data = new Date(valor + 'T00:00:00Z');
  return !Number.isNaN(data.getTime()) && data.toISOString().slice(0, 10) === valor;
}

export function lerRecebimento(serializado: string | null): RecebimentoPendente | null {
  if (!serializado) return null;
  try {
    const dado = JSON.parse(serializado);
    const identificador = (valor: unknown) => typeof valor === 'string' && uuid.test(valor);
    if (
      identificador(dado?.compraId) &&
      identificador(dado.chave) &&
      identificador(dado.corpo?.versao) &&
      (dado.corpo.semControleValidade === undefined ||
        typeof dado.corpo.semControleValidade === 'boolean') &&
      (dado.corpo.semControleValidade !== true || dado.corpo.validades?.length === 0) &&
      Array.isArray(dado.corpo.validades) &&
      dado.corpo.validades.length <= 1500 &&
      dado.corpo.validades.every((v: unknown) => {
        const validade = v as ReceberCompraInput['validades'][number] | null;
        return (
          identificador(validade?.componenteId) &&
          typeof validade?.dataValidade === 'string' &&
          dataCivilValida(validade.dataValidade)
        );
      })
    )
      return dado;
  } catch {
    return null;
  }
  return null;
}
