export function emCentesimos(entrada: string, moeda: boolean, permitirZero = false): string | null {
  const valor = moeda ? entrada.trim().replace(/^R\$\s*/u, '') : entrada.trim();
  const normalizado = moeda && /^\d{1,3}(?:\.\d{3})+(?:,\d{1,2})?$/.test(valor)
    ? valor.replaceAll('.', '') : valor;
  const partes = /^(\d{1,19})(?:[,.](\d{1,2}))?$/.exec(normalizado);
  if (!partes) return null;
  const inteiro = BigInt(partes[1] + (partes[2] ?? '').padEnd(2, '0'));
  return (permitirZero || inteiro > 0n) && inteiro <= 9_223_372_036_854_775_807n ? inteiro.toString() : null;
}

export function textoDecimal(valor: string): string {
  return `${BigInt(valor) / 100n},${String(BigInt(valor) % 100n).padStart(2, '0')}`;
}

export function paraDataLocal(utc: string | null | undefined): string {
  if (!utc) return '';
  const data = new Date(utc);
  const dois = (n: number) => String(n).padStart(2, '0');
  return `${data.getFullYear()}-${dois(data.getMonth() + 1)}-${dois(data.getDate())}T${dois(data.getHours())}:${dois(data.getMinutes())}`;
}

export function converterData(local: string, original: string | null | undefined): string | null | undefined {
  if (!local) return null;
  if (original && local === paraDataLocal(original)) return original;
  const data = new Date(local);
  if (Number.isNaN(data.getTime())) return undefined;
  const utc = data.toISOString();
  return paraDataLocal(utc) === local ? utc : undefined;
}
