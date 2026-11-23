import { DOCUMENT } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom, timeout } from 'rxjs';
import { erroPostal } from '../core/cotacoes-correios.service';
import { AuthService } from '../core/auth/auth.service';
import { CarrinhoService } from './carrinho.service';
import {
  ItemCompra,
  PedidoCliente,
  PedidosService,
  PrecoAtualPedido,
  itensCompraValidos,
  mesmosItensCompra,
  uuidValido,
} from './pedidos.service';

const chaveLocal = 'xaquiamuni:tentativa-compra:v1';

interface TentativaCompra {
  chave: string;
  vinculo: string;
  itens: ItemCompra[];
  ofertaEntregaId?: string;
  postal?: { token: string; cotacaoId: string };
}

export type ResultadoCheckout =
  | { estado: 'login' }
  | { estado: 'erro'; mensagem: string }
  | { estado: 'preco_alterado' }
  | { estado: 'oferta_alterada' }
  | { estado: 'sucesso'; pedido: PedidoCliente };

@Injectable({ providedIn: 'root' })
export class CheckoutService {
  private readonly janela = inject(DOCUMENT).defaultView;
  private readonly pedidos = inject(PedidosService);
  private readonly auth = inject(AuthService);
  private readonly carrinho = inject(CarrinhoService);
  private readonly pendenteInterna = signal(lerTentativaCompra(this.janela));
  private readonly enviandoInterno = signal(false);
  readonly pendente = this.pendenteInterna.asReadonly();
  readonly enviando = this.enviandoInterno.asReadonly();

  atualizarTentativa(): void {
    this.pendenteInterna.set(lerTentativaCompra(this.janela));
  }

  async enviar(itens: ItemCompra[], recuperar: boolean, ofertaEntregaId?: string, postal?: TentativaCompra['postal']): Promise<ResultadoCheckout | null> {
    if (this.enviando()) {
      return null;
    }

    this.enviandoInterno.set(true);
    try {
      const executar = () => this.processar(itens, recuperar, ofertaEntregaId, postal);
      const locks = this.janela?.navigator.locks;
      return await (locks
        ? locks.request(chaveLocal, { ifAvailable: true }, (lock) =>
            lock
              ? executar()
              : erro('A compra está sendo processada em outra aba. Aguarde e tente novamente.'),
          )
        : executar());
    } catch (falha) {
      if (falha instanceof HttpErrorResponse) {
        if (falha.status === 401) return { estado: 'login' };
        if (falha.status === 403)
          return erro('Entre com uma conta de cliente habilitada para comprar.');
      }
      return erro(
        'Não foi possível preparar a compra. Verifique a conexão e o armazenamento do navegador.',
      );
    } finally {
      this.atualizarTentativa();
      this.enviandoInterno.set(false);
    }
  }

  private async processar(itens: ItemCompra[], recuperar: boolean, ofertaEntregaId?: string, postal?: TentativaCompra['postal']): Promise<ResultadoCheckout> {
    const armazenamento = this.janela?.localStorage;
    if (!armazenamento)
      return erro('Permita o armazenamento local para registrar a tentativa de compra.');
    const anterior = lerTentativaCompra(this.janela);
    if (!anterior && armazenamento.getItem(chaveLocal) !== null) {
      return erro(
        'O registro de uma tentativa anterior está inválido. Não foi enviado outro pedido.',
      );
    }
    this.pendenteInterna.set(anterior);
    if (anterior && !recuperar) {
      return erro('Há uma tentativa pendente. Use a recuperação antes de enviar o carrinho atual.');
    }
    if (!anterior && recuperar) {
      return erro('A tentativa já foi resolvida nesta sessão do navegador.');
    }
    if (!anterior && !postal && !itensCompraValidos(itens)) {
      return erro('Revise os itens e as quantidades no carrinho.');
    }

    if (!anterior && !postal) {
      const carrinho = this.carrinho.sincronizarItens();
      if (carrinho.some((item) => item.disponivel === false)) {
        return erro('Remova os itens indisponíveis antes de enviar o carrinho.');
      }
      const atuais = carrinho.map((item) => ({
        produtoId: item.produtoId,
        produtoVarianteId: item.varianteId,
        quantidade: item.quantidade,
        precoAceitoCentavos: item.precoCentavos,
      }));
      if (!mesmosItensCompra(itens, atuais) || itens.some(item => item.precoAceitoCentavos !== undefined &&
        !atuais.some(atual => atual.produtoVarianteId === item.produtoVarianteId &&
          atual.precoAceitoCentavos === item.precoAceitoCentavos))) {
        return erro('O carrinho mudou. Revise os preços e as quantidades antes de finalizar.');
      }
    }

    // O token é vinculado à sessão. Conferimos a conta depois de obter o token.
    const { requestToken } = await firstValueFrom(this.pedidos.obterToken());
    const sessao = await firstValueFrom(this.auth.obterSessao().pipe(timeout(30_000)));
    if (!sessao) {
      return { estado: 'login' };
    }
    if (!uuidValido(sessao.userId) || !this.janela?.crypto.subtle || !requestToken) {
      return erro('Não foi possível preparar o envio seguro do pedido.');
    }

    const hash = await this.janela.crypto.subtle.digest(
      'SHA-256',
      new TextEncoder().encode(`xaquiamuni:compra:${sessao.userId.toLowerCase()}`),
    );
    const vinculo = Array.from(new Uint8Array(hash), (byte) =>
      byte.toString(16).padStart(2, '0'),
    ).join('');
    if (anterior && vinculo !== anterior.vinculo) {
      return erro('Entre com a conta usada na tentativa original para recuperar a compra.');
    }

    const tentativa = anterior ?? { chave: this.janela.crypto.randomUUID(), vinculo, itens, ofertaEntregaId, postal };
    if (!anterior) {
      armazenamento.setItem(chaveLocal, JSON.stringify(tentativa));
      this.pendenteInterna.set(tentativa);
    }

    let pedido: PedidoCliente;
    try {
      pedido = await firstValueFrom(
        this.pedidos.criar(tentativa.itens, tentativa.chave, requestToken, tentativa.ofertaEntregaId, tentativa.postal?.token),
      );
    } catch (falha) {
      return this.tratarFalha(falha, !!anterior, armazenamento, !!tentativa.postal);
    }

    if (tentativa.postal || this.carrinho.confirmarCompra(tentativa.chave, pedido.itens)) {
      try {
        armazenamento.removeItem(chaveLocal);
      } catch {
        // A repetição permanece segura, inclusive no ajuste do carrinho.
      }
    }
    return { estado: 'sucesso', pedido };
  }

  private tratarFalha(
    falha: unknown,
    repeticao: boolean,
    armazenamento: Storage,
    postal: boolean,
  ): ResultadoCheckout {
    if (falha instanceof HttpErrorResponse) {
      const dados = falha.error as { codigo?: string; precosAtuais?: PrecoAtualPedido[] } | null;
      const codigo = dados?.codigo;
      if (falha.status === 401) {
        return { estado: 'login' };
      }
      if (falha.status === 409 && codigo === 'itens_indisponiveis') {
        armazenamento.removeItem(chaveLocal);
        return erro(postal ? erroPostal(falha) :
          'Não há estoque para todos os itens solicitados. Revise as opções e quantidades no carrinho.');
      }
      if (falha.status === 409 && codigo === 'preco_alterado') {
        armazenamento.removeItem(chaveLocal);
        if (!postal && Array.isArray(dados?.precosAtuais))
          this.carrinho.atualizarPrecos(dados.precosAtuais.filter(p => p && typeof p === 'object'));
        return { estado: 'preco_alterado' };
      }
      if (postal && falha.status === 409 && codigo?.startsWith('cotacao_correios_')) {
        armazenamento.removeItem(chaveLocal);
        return erro(erroPostal(falha));
      }
      if (falha.status === 409 && codigo === 'oferta_entrega_invalida') {
        armazenamento.removeItem(chaveLocal);
        return { estado: 'oferta_alterada' };
      }
      if (falha.status === 409 && codigo === 'chave_em_uso') {
        return erro(
          'A tentativa entrou em conflito com um pedido registrado. Ela foi preservada para evitar uma segunda compra.',
        );
      }
      if (falha.status === 403) {
        if (!repeticao) armazenamento.removeItem(chaveLocal);
        return erro(
          'Sua conta não está habilitada para fazer este pedido. Confira seu cadastro e a confirmação do e-mail.',
        );
      }
      if ([400, 413, 415].includes(falha.status)) {
        if (!repeticao) armazenamento.removeItem(chaveLocal);
        return erro('A solicitação foi recusada. Confira as quantidades e tente novamente.');
      }
    }

    return erro(
      'Ainda não foi possível confirmar o resultado. Repita a tentativa original para verificar o pedido.',
    );
  }
}

export function lerTentativaCompra(janela: Window | null): TentativaCompra | null {
  try {
    const bruto = janela?.localStorage.getItem(chaveLocal);
    const tentativa = bruto ? JSON.parse(bruto) : null;
    if (
      !tentativa ||
      !uuidValido(tentativa.chave) ||
      typeof tentativa.vinculo !== 'string' ||
      !/^[a-f0-9]{64}$/.test(tentativa.vinculo) ||
      (tentativa.postal === undefined ? !itensCompraValidos(tentativa.itens) :
        !uuidValido(tentativa.postal?.cotacaoId) || typeof tentativa.postal?.token !== 'string' ||
        !tentativa.postal.token || tentativa.ofertaEntregaId !== undefined ||
        !Array.isArray(tentativa.itens) || tentativa.itens.length !== 0) ||
      (tentativa.ofertaEntregaId !== undefined &&
        (typeof tentativa.ofertaEntregaId !== 'string' || !tentativa.ofertaEntregaId || tentativa.ofertaEntregaId.length > 16000))
    ) {
      return null;
    }
    return {
      chave: tentativa.chave,
      vinculo: tentativa.vinculo,
      ofertaEntregaId: tentativa.ofertaEntregaId,
      postal: tentativa.postal,
      itens: tentativa.itens.map(({ produtoId, produtoVarianteId, quantidade, precoAceitoCentavos }: ItemCompra) => ({
        produtoId,
        produtoVarianteId,
        quantidade,
        ...(precoAceitoCentavos === undefined ? {} : { precoAceitoCentavos }),
      })),
    };
  } catch {
    return null;
  }
}

function erro(mensagem: string): ResultadoCheckout {
  return { estado: 'erro', mensagem };
}
