import { DOCUMENT } from '@angular/common';
import { Injectable, computed, inject, signal } from '@angular/core';
import { ProdutoCatalogoDetalhe } from './catalogo-publico.service';

const chaveCarrinho = 'xaquiamuni:carrinho:v1';

export interface ItemCarrinho {
  produtoId: string;
  varianteId: string;
  nome: string;
  variante: string | null;
  imagem: string | null;
  precoCentavos: number;
  quantidade: number;
  disponivel: boolean | null;
}

export type ResultadoCarrinho = 'ok' | 'indisponivel' | 'invalido';

const moeda = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' });

export function formatarCentavos(centavos: number): string {
  const valor = BigInt(centavos);
  return moeda
    .formatToParts(valor / 100n)
    .map((parte) =>
      parte.type === 'fraction' ? String(valor % 100n).padStart(2, '0') : parte.value,
    )
    .join('');
}

@Injectable({ providedIn: 'root' })
export class CarrinhoService {
  private readonly documento = inject(DOCUMENT);
  private readonly itensInternos = signal<ItemCarrinho[]>(this.restaurar());
  private readonly falhaPersistencia = signal(false);
  readonly itens = this.itensInternos.asReadonly();
  readonly persistenciaIndisponivel = this.falhaPersistencia.asReadonly();
  readonly quantidadeTotal = computed(() => somarQuantidade(this.itens()));
  readonly subtotalCentavos = computed(() => somarCentavos(this.itens()));

  adicionar(produto: ProdutoCatalogoDetalhe, varianteId: string): ResultadoCarrinho {
    const opcao = produto.opcoes.find((item) => item.id === varianteId);
    if (opcao?.disponivel !== true) {
      return 'indisponivel';
    }

    const anterior = this.itens().find((item) => item.varianteId === varianteId);
    const imagem = produto.galeria.find((foto) => foto.opcaoIds.includes(varianteId));
    const textoPreco = /^(\d+)(?:\.(\d{1,2}))?$/.exec(String(opcao.precoVenda));
    const precoCentavos = textoPreco
      ? Number(`${textoPreco[1]}${(textoPreco[2] ?? '').padEnd(2, '0')}`)
      : NaN;
    const item = lerItem({
      produtoId: produto.id,
      varianteId,
      nome: produto.nome,
      variante:
        opcao.identificacao ??
        (opcao.caracteristicas.map((valor) => valor.valorNome).join(' · ') || null),
      imagem: imagem?.conteudoUrl ?? produto.fotoPrincipal.conteudoUrl,
      precoCentavos,
      quantidade: (anterior?.quantidade ?? 0) + 1,
    });
    if (!item || (anterior && anterior.produtoId !== produto.id)) {
      return 'invalido';
    }

    item.disponivel = true;
    return this.gravar(
      anterior
        ? this.itens().map((atual) => (atual === anterior ? item : atual))
        : [...this.itens(), item],
    );
  }

  alterarQuantidade(varianteId: string, quantidade: number): ResultadoCarrinho {
    const item = this.itens().find((atual) => atual.varianteId === varianteId);
    if (!item || !inteiroNaoNegativo(quantidade) || quantidade === 0) {
      return 'invalido';
    }
    if (quantidade > item.quantidade && item.disponivel !== true) {
      return 'indisponivel';
    }
    return this.gravar(
      this.itens().map((atual) => (atual === item ? { ...atual, quantidade } : atual)),
    );
  }

  remover(varianteId: string): void {
    this.gravar(this.itens().filter((item) => item.varianteId !== varianteId));
  }

  esvaziar(): void {
    this.gravar([]);
  }

  atualizarDisponibilidade(produto: ProdutoCatalogoDetalhe): void {
    const opcoes = new Map(produto.opcoes.map((opcao) => [opcao.id, opcao]));
    this.itensInternos.update((itens) =>
      itens.map((item) => {
        if (item.produtoId !== produto.id) {
          return item;
        }
        const opcao = opcoes.get(item.varianteId);
        const disponivel = opcao?.disponivel;
        return {
          ...item,
          disponivel: opcao ? (typeof disponivel === 'boolean' ? disponivel : null) : false,
        };
      }),
    );
  }

  sinalizarDisponibilidade(produtoId: string, disponivel: false | null): void {
    this.itensInternos.update((itens) =>
      itens.map((item) => (item.produtoId === produtoId ? { ...item, disponivel } : item)),
    );
  }

  private gravar(itens: ItemCarrinho[]): ResultadoCarrinho {
    if (!inteiroNaoNegativo(somarCentavos(itens)) || !inteiroNaoNegativo(somarQuantidade(itens))) {
      return 'invalido';
    }
    this.itensInternos.set(itens);
    try {
      const armazenamento = this.documento.defaultView?.localStorage;
      if (armazenamento) {
        armazenamento.setItem(
          chaveCarrinho,
          JSON.stringify(itens.map(({ disponivel: _, ...item }) => item)),
        );
      }
      this.falhaPersistencia.set(!armazenamento);
    } catch {
      this.falhaPersistencia.set(true);
    }
    return 'ok';
  }

  private restaurar(): ItemCarrinho[] {
    try {
      const bruto = this.documento.defaultView?.localStorage.getItem(chaveCarrinho);
      const dados: unknown = bruto ? JSON.parse(bruto) : null;
      if (!Array.isArray(dados)) {
        return [];
      }

      const itens = new Map<string, ItemCarrinho>();
      let subtotal = 0;
      let quantidade = 0;
      for (const dado of dados) {
        const item = lerItem(dado);
        if (!item || itens.has(item.varianteId)) {
          continue;
        }
        const novoSubtotal = subtotal + item.precoCentavos * item.quantidade;
        const novaQuantidade = quantidade + item.quantidade;
        if (inteiroNaoNegativo(novoSubtotal) && inteiroNaoNegativo(novaQuantidade)) {
          subtotal = novoSubtotal;
          quantidade = novaQuantidade;
          itens.set(item.varianteId, item);
        }
      }
      return [...itens.values()];
    } catch {
      return [];
    }
  }
}

function lerItem(dado: unknown): ItemCarrinho | null {
  if (!dado || typeof dado !== 'object') {
    return null;
  }
  const { produtoId, varianteId, nome, variante, imagem, precoCentavos, quantidade } =
    dado as Record<string, unknown>;
  if (
    !identificador(produtoId) ||
    !identificador(varianteId) ||
    typeof nome !== 'string' ||
    !nome.trim() ||
    (variante !== null && typeof variante !== 'string') ||
    !inteiroNaoNegativo(precoCentavos) ||
    !inteiroNaoNegativo(quantidade) ||
    quantidade === 0
  ) {
    return null;
  }
  return {
    produtoId,
    varianteId,
    nome,
    variante,
    precoCentavos,
    quantidade,
    imagem:
      typeof imagem === 'string' &&
      /^\/api\/catalogo\/produtos\/[a-zA-Z0-9_-]+\/imagens\/[a-zA-Z0-9_-]+\/conteudo$/.test(imagem)
        ? imagem
        : null,
    disponivel: null,
  };
}

function identificador(valor: unknown): valor is string {
  return typeof valor === 'string' && /^[a-zA-Z0-9_-]{1,128}$/.test(valor);
}

function inteiroNaoNegativo(valor: unknown): valor is number {
  return typeof valor === 'number' && Number.isSafeInteger(valor) && valor >= 0;
}

function somarQuantidade(itens: ItemCarrinho[]): number {
  return itens.reduce((total, item) => total + item.quantidade, 0);
}

function somarCentavos(itens: ItemCarrinho[]): number {
  return itens.reduce((total, item) => total + item.precoCentavos * item.quantidade, 0);
}
