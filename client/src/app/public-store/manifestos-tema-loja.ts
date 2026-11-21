export const TEMA_ORIGINAL = 'original';

interface TemaSazonal {
  readonly arte: string;
  readonly titulo: string;
  readonly chamada: string;
  readonly descricao: string;
  readonly editorial: { readonly titulo: string; readonly texto: string };
  readonly tokens: Readonly<Record<string, string>>;
}

export interface ManifestoTemaLoja {
  readonly chave: string;
  readonly nome: string;
  readonly disponivel: boolean;
  readonly sazonal?: TemaSazonal;
}

function sazonal(
  chave: string, nome: string, titulo: string, chamada: string, descricao: string,
  editorial: TemaSazonal['editorial'], tokens: TemaSazonal['tokens'],
): ManifestoTemaLoja {
  return { chave, nome, disponivel: true, sazonal: {
    arte: `/temas/${chave}.svg`, titulo, chamada, descricao, editorial,
    tokens: {
      '--brand-action': tokens['--brand-green'],
      '--brand-action-hover': tokens['--brand-green-hover'],
      '--brand-link': tokens['--brand-green'],
      ...tokens,
    },
  } };
}

const manifestos = new Map<string, ManifestoTemaLoja>([
  { chave: TEMA_ORIGINAL, nome: 'Original', disponivel: true },
  sazonal('natal', 'Natal', 'Presentes feitos com carinho', 'Natal artesanal',
    'Sabonetes artesanais e velas aromáticas para compartilhar momentos de cuidado.',
    { titulo: 'Um gesto de carinho', texto: 'Para receber em casa, retribuir um abraço ou lembrar de quem está longe, escolha um presente feito à mão.' },
    { '--brand-bg': '#fbf4e8', '--brand-surface': '#fff8ed', '--brand-green': '#21483b',
      '--brand-green-hover': '#17362d', '--brand-action': '#802c3d', '--brand-action-hover': '#632133',
      '--brand-focus': '#8c3446', '--brand-sand': '#f1ddc1', '--brand-sage': '#e3ebe0',
      '--brand-gold-light': '#efd295', '--tema-painel': '#782b3d', '--tema-texto': '#fff7e9',
      '--tema-topo': '#fff8ed', '--brand-line': '#cdab90', '--brand-radius': '1.65rem' }),
  sazonal('black-friday', 'Black Friday', 'O artesanal em destaque', 'Black Friday',
    'Conheça os sabonetes e as velas de produção própria da Xaquiamuni.',
    { titulo: 'Escolha pelo que você sente', texto: 'Explore fragrâncias, apresentações e os preços disponíveis no catálogo.' },
    { '--brand-bg': '#eeeae3', '--brand-surface': '#faf7ef', '--brand-ink': '#242128',
      '--brand-green': '#302738', '--brand-green-hover': '#161419', '--brand-focus': '#74501c',
      '--brand-sand': '#e6d6b8', '--brand-sage': '#e3dce5', '--brand-gold-light': '#f1d49b',
      '--brand-line': '#ad9c84', '--brand-radius': '.35rem', '--brand-radius-pill': '.35rem',
      '--brand-display': 'system-ui, sans-serif', '--tema-painel': '#17171c', '--tema-texto': '#fff6e3',
      '--tema-topo': '#faf7ef' }),
  sazonal('pascoa', 'Páscoa', 'Uma pausa delicada', 'Páscoa artesanal',
    'Notas suaves, cuidado em pequenos gestos e a leveza de um momento para você.',
    { titulo: 'Um carinho para compartilhar', texto: 'Entre aromas e texturas, encontre sabonetes artesanais e velas aromáticas para um presente delicado.' },
    { '--brand-bg': '#f5eef7', '--brand-surface': '#fff9ed', '--brand-green': '#523962',
      '--brand-green-hover': '#3f294e', '--brand-focus': '#6b447c', '--brand-sand': '#eadcf0',
      '--brand-sage': '#e1ebdb', '--brand-gold-light': '#e8d9ad', '--brand-line': '#c7b5cb',
      '--brand-radius': '1.8rem', '--tema-painel': '#e9ddef', '--tema-texto': '#3f294e',
      '--tema-topo': '#fcf6f4' }),
  sazonal('ano-novo', 'Ano Novo', 'Um novo começo, no seu ritmo', 'Ano Novo',
    'Abra espaço para rituais simples de renovação, com aromas e cuidado feitos à mão.',
    { titulo: 'Acenda o primeiro momento do dia', texto: 'Uma vela aromática ou um sabonete artesanal pode marcar o início de uma rotina de cuidado.' },
    { '--brand-bg': '#faf5e8', '--brand-surface': '#fffbf2', '--brand-green': '#57431e',
      '--brand-green-hover': '#433218', '--brand-focus': '#775717', '--brand-sand': '#eee0bd',
      '--brand-sage': '#f1e7cf', '--brand-gold-light': '#f5dc9f', '--brand-line': '#c5aa73',
      '--brand-radius': '.85rem', '--tema-painel': '#f6edda', '--tema-texto': '#493619',
      '--tema-topo': '#fffbf2' }),
  sazonal('dia-das-maes', 'Dia das Mães', 'Carinho em forma de presente', 'Dia das Mães',
    'Sabonetes artesanais e velas aromáticas para celebrar quem cuida de você.',
    { titulo: 'Para quem faz do cuidado um abraço', texto: 'Escolha uma fragrância e ofereça um momento de descanso, pensado com carinho.' },
    { '--brand-bg': '#fcf1f3', '--brand-surface': '#fff8ee', '--brand-green': '#63334b',
      '--brand-green-hover': '#4d263b', '--brand-focus': '#893e60', '--brand-sand': '#f2dce4',
      '--brand-sage': '#f4e4d3', '--brand-gold-light': '#efd392', '--brand-line': '#d3a9b8',
      '--brand-radius': '1.4rem', '--tema-painel': '#f3dfe4', '--tema-texto': '#532a40',
      '--tema-topo': '#fff8f1' }),
].map(tema => [tema.chave, tema]));

export function buscarManifestoTemaLoja(chave: unknown): ManifestoTemaLoja | undefined {
  const tema = typeof chave === 'string' ? manifestos.get(chave) : undefined;
  return tema?.disponivel ? tema : undefined;
}

export function resolverTemaLoja(chave: unknown): string {
  return buscarManifestoTemaLoja(chave)?.chave ?? TEMA_ORIGINAL;
}
