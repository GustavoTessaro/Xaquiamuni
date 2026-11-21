export const TEMA_ORIGINAL = 'original';

export interface ManifestoTemaLoja {
  /** Seletor de tokens CSS aplicados ao contêiner da loja. */
  readonly chave: string;
  readonly nome: string;
  readonly descricao: string;
  readonly hero?: {
    readonly imagemDesktop: string;
    readonly imagemMobile?: string;
    readonly titulo: string;
    readonly subtitulo?: string;
    readonly cta?: { readonly rotulo: string; readonly rota: string };
  };
  readonly decoracoes?: readonly string[];
  readonly composicaoHome?: readonly string[];
  readonly estilos?: {
    readonly cabecalho?: string;
    readonly rodape?: string;
    readonly cards?: string;
  };
}

// A identidade original usa os tokens de brand.css e os componentes públicos atuais.
// Somente temas com assets homologados entram nesta lista.
const manifestos: ReadonlyMap<string, ManifestoTemaLoja> = new Map([
  [TEMA_ORIGINAL, { chave: TEMA_ORIGINAL, nome: 'Original', descricao: 'Identidade visual da Xaquiamuni.' }],
]);

export function resolverTemaLoja(chave: unknown): string {
  return typeof chave === 'string' ? manifestos.get(chave)?.chave ?? TEMA_ORIGINAL : TEMA_ORIGINAL;
}
