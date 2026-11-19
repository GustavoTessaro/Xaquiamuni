const inteiroPt = new Intl.NumberFormat('pt-BR');

export function decimalEscalado(
  texto: string,
  casas: number,
  digitosInteiros: number,
): bigint | null {
  const partes = texto.trim().replace(',', '.').split('.');
  const [inteiro, fracao = ''] = partes;
  if (
    partes.length > 2 ||
    !/^\d+$/.test(inteiro) ||
    inteiro.length > digitosInteiros ||
    !/^\d*$/.test(fracao) ||
    fracao.length > casas
  )
    return null;
  return BigInt(inteiro + fracao.padEnd(casas, '0'));
}

export function decimalTexto(valor: bigint, casas: number): string {
  const texto = valor.toString().padStart(casas + 1, '0');
  if (casas === 0) return texto;
  const fracao = texto.slice(-casas).replace(/0+$/, '');
  return texto.slice(0, -casas) + (fracao ? '.' + fracao : '');
}

export function exibirReais(centavos: bigint): string {
  return (
    'R$ ' + inteiroPt.format(centavos / 100n) + ',' + (centavos % 100n).toString().padStart(2, '0')
  );
}
