import { DOCUMENT } from '@angular/common';
import { Injectable, computed, inject, signal } from '@angular/core';
import { limiteQuantidadePedido, type ItemCompra } from './pedidos.service';
import { PrecoCatalogo, ProdutoCatalogoDetalhe, ProdutoCatalogoOpcao } from './catalogo-publico.service';
import { precoValido } from './catalogo-formatacao';

const chaveCarrinho = 'xaquiamuni:carrinho:v1';

export interface ItemCarrinho {
  produtoId: string;
  varianteId: string;
  nome: string;
  variante: string | null;
  imagem: string | null;
  precoCentavos: string;
  preco?: PrecoCatalogo;
  quantidade: number;
  disponivel: boolean | null;
}

export type ResultadoCarrinho = 'ok' | 'indisponivel' | 'invalido';

const moeda = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' });

export function formatarCentavos(centavos: number | string | bigint): string {
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
  private compraAplicada: string | null = null;
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
    const precoCentavos = lerPrecoOpcao(opcao);
    const item = lerItem({
      produtoId: produto.id,
      varianteId,
      nome: produto.nome,
      variante:
        opcao.identificacao ??
        (opcao.caracteristicas.map((valor) => valor.valorNome).join(' · ') || null),
      imagem: imagem?.conteudoUrl ?? produto.fotoPrincipal.conteudoUrl,
      precoCentavos,
      preco: opcao.preco,
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
    if (!item || !quantidadeValida(quantidade)) {
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

  sincronizarItens(): ItemCarrinho[] {
    if (!this.persistenciaIndisponivel()) {
      const atuais = new Map(this.itens().map((item) => [item.varianteId, item]));
      const salvos = this.restaurar();
      this.itensInternos.set(
        salvos.map((item) => {
          const atual = atuais.get(item.varianteId);
          return atual?.produtoId === item.produtoId
            ? { ...item, disponivel: atual.disponivel }
            : item;
        }),
      );
    }
    return this.itens();
  }

  confirmarCompra(chave: string, comprados: readonly ItemCompra[]): boolean {
    this.sincronizarItens();
    const porVariante = new Map(
      comprados.map((item) => [item.produtoVarianteId.toLowerCase(), item]),
    );
    const restantes =
      this.compraAplicada === chave
        ? this.itens()
        : this.itens().flatMap((item) => {
            const comprado = porVariante.get(item.varianteId.toLowerCase());
            if (!comprado || comprado.produtoId.toLowerCase() !== item.produtoId.toLowerCase()) {
              return [item];
            }
            const quantidade = item.quantidade - comprado.quantidade;
            return quantidade > 0 ? [{ ...item, quantidade }] : [];
          });

    // A marca e as quantidades são persistidas juntas para impedir baixa repetida no carrinho.
    this.compraAplicada = chave;
    this.gravar(restantes);
    return !this.persistenciaIndisponivel();
  }

  atualizarDisponibilidade(produto: ProdutoCatalogoDetalhe): boolean {
    const opcoes = new Map(produto.opcoes.map((opcao) => [opcao.id, opcao]));
    const itens = this.sincronizarItens();
    const atualizados = itens.map((item) => {
      if (item.produtoId !== produto.id) return item;
      const opcao = opcoes.get(item.varianteId);
      const precoCentavos = opcao && lerPrecoOpcao(opcao);
      return {
        ...item,
        ...(precoCentavos ? { precoCentavos, preco: opcao?.preco } : {}),
        disponivel: opcao?.disponivel ?? false,
      };
    });
    const mudou = itens.some((item, i) => item.precoCentavos !== atualizados[i].precoCentavos);
    if (mudou || itens.some((item, i) => item.preco?.promocaoId !== atualizados[i].preco?.promocaoId ||
      item.preco?.regularCentavos !== atualizados[i].preco?.regularCentavos)) {
      this.gravar(atualizados);
    } else {
      this.itensInternos.set(atualizados);
    }
    return mudou;
  }

  atualizarPrecos(precos: readonly { produtoId: string; produtoVarianteId: string; preco: PrecoCatalogo }[]): void {
    const itens = this.sincronizarItens().map((item) => {
      const novo = precos.find(p => p.produtoId === item.produtoId && p.produtoVarianteId === item.varianteId);
      return novo && precoValido(novo.preco?.finalCentavos)
        ? { ...item, precoCentavos: novo.preco.finalCentavos, preco: novo.preco } : item;
    });
    this.gravar(itens);
  }

  subtotalItem(item: ItemCarrinho): bigint {
    return BigInt(item.precoCentavos) * BigInt(item.quantidade);
  }

  sinalizarDisponibilidade(produtoId: string, disponivel: false | null): void {
    this.itensInternos.update((itens) =>
      itens.map((item) => (item.produtoId === produtoId ? { ...item, disponivel } : item)),
    );
  }

  private gravar(itens: ItemCarrinho[]): ResultadoCarrinho {
    if (!inteiroNaoNegativo(somarQuantidade(itens))) {
      return 'invalido';
    }
    this.itensInternos.set(itens);
    try {
      const armazenamento = this.documento.defaultView?.localStorage;
      if (armazenamento) {
        const dados = itens.map(({ disponivel: _, ...item }) => item);
        armazenamento.setItem(
          chaveCarrinho,
          JSON.stringify(
            this.compraAplicada ? { itens: dados, compraAplicada: this.compraAplicada } : dados,
          ),
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
      const salvo = bruto ? JSON.parse(bruto) : null;
      const dados: unknown = Array.isArray(salvo) ? salvo : salvo?.itens;
      if (
        typeof salvo?.compraAplicada === 'string' &&
        /^[a-f0-9-]{36}$/i.test(salvo.compraAplicada)
      ) {
        this.compraAplicada = salvo.compraAplicada;
      }
      if (!Array.isArray(dados)) {
        return [];
      }

      const itens = new Map<string, ItemCarrinho>();
      let quantidade = 0;
      for (const dado of dados) {
        const item = lerItem(dado);
        if (!item || itens.has(item.varianteId)) {
          continue;
        }
        const novaQuantidade = quantidade + item.quantidade;
        if (inteiroNaoNegativo(novaQuantidade)) {
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
  const { produtoId, varianteId, nome, variante, imagem, precoCentavos, preco, quantidade } =
    dado as Record<string, unknown>;
  const centavos = inteiroNaoNegativo(precoCentavos) ? String(precoCentavos) : precoCentavos;
  if (
    !identificador(produtoId) ||
    !identificador(varianteId) ||
    typeof nome !== 'string' ||
    !nome.trim() ||
    (variante !== null && typeof variante !== 'string') ||
    !precoValido(centavos) ||
    !quantidadeValida(quantidade)
  ) {
    return null;
  }
  return {
    produtoId,
    varianteId,
    nome,
    variante,
    precoCentavos: centavos,
    preco: lerPrecoCatalogo(preco, centavos),
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

function somarCentavos(itens: ItemCarrinho[]): bigint {
  return itens.reduce((total, item) => total + BigInt(item.precoCentavos) * BigInt(item.quantidade), 0n);
}

function quantidadeValida(valor: unknown): valor is number {
  return inteiroNaoNegativo(valor) && valor > 0 && valor <= limiteQuantidadePedido;
}

function lerPrecoOpcao(opcao: ProdutoCatalogoOpcao): string | null {
  if (opcao.preco) return precoValido(opcao.preco.finalCentavos) ? opcao.preco.finalCentavos : null;
  const partes = /^(\d+)(?:\.(\d{1,2}))?$/.exec(String(opcao.precoVenda));
  if (!partes) return null;
  const centavos = BigInt(`${partes[1]}${(partes[2] ?? '').padEnd(2, '0')}`);
  return centavos > 0n && centavos <= BigInt(Number.MAX_SAFE_INTEGER) ? String(centavos) : null;
}

function lerPrecoCatalogo(valor: unknown, centavos: string): PrecoCatalogo | undefined {
  const preco = valor as PrecoCatalogo | null;
  return preco?.finalCentavos === centavos && precoValido(preco.regularCentavos) &&
    typeof preco.economiaCentavos === 'string' && /^(0|[1-9]\d{0,18})$/.test(preco.economiaCentavos) &&
    Number.isInteger(preco.offPontosBase) &&
    (preco.promocaoId === null || typeof preco.promocaoId === 'string') ? preco : undefined;
}
