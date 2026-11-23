import { DefaultUrlSerializer, ParamMap, UrlTree } from '@angular/router';
import { uuidValido } from '../../public-store/pedidos.service';

const urls = new DefaultUrlSerializer();
const rotasSimples = new Set(['/', '/produtos', '/carrinho', '/checkout', '/minha-conta', '/pedidos', '/cotacoes-correios']);
const rotasComId = new Set(['produtos', 'pedidos', 'cotacoes-correios']);

export function validarDestinoLoja(destino: string): UrlTree {
  const caminho = destino.split(/[?#]/, 1)[0];
  const [, rota, id, ...extras] = caminho.split('/');
  if (!rotasSimples.has(caminho) &&
      !(caminho.startsWith('/') && rotasComId.has(rota) && uuidValido(id) && !extras.length)) {
    return urls.parse('/');
  }

  try {
    const arvore = urls.parse(destino);
    const { busca, categoriaId, pagina } = arvore.queryParams;
    arvore.queryParams = {
      ...(caminho === '/produtos' && typeof busca === 'string' && busca && { busca }),
      ...(caminho === '/produtos' && uuidValido(categoriaId) && { categoriaId }),
      ...((caminho === '/pedidos' || rota === 'pedidos') &&
        typeof pagina === 'string' && /^[1-9]\d{0,8}$/.test(pagina) && { pagina }),
    };
    return arvore;
  } catch {
    return urls.parse('/');
  }
}

export function destinoLoginCliente(params: ParamMap): UrlTree | null {
  if (params.has('destino')) return validarDestinoLoja(params.get('destino') ?? '');

  // Preserva os links existentes do checkout, cadastro e confirmação de e-mail.
  const retorno = params.get('retorno');
  const pedido = params.get('pedido');
  const cotacao = params.get('cotacao');
  if (retorno === 'checkout') return validarDestinoLoja('/checkout');
  if (retorno === 'cotacoes-correios') {
    return validarDestinoLoja(cotacao === null ? '/cotacoes-correios' : '/cotacoes-correios/' + cotacao);
  }
  if (pedido !== null) return validarDestinoLoja('/pedidos/' + pedido);
  if (retorno === 'pedidos') return validarDestinoLoja('/pedidos');
  return retorno === null ? null : validarDestinoLoja('/');
}
