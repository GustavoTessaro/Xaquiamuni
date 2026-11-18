import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
  TestRequest,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { AuthService } from '../core/auth/auth.service';
import { LoginComponent } from '../login/login.component';
import { CarrinhoService, formatarCentavos } from './carrinho.service';
import { CatalogoPublicoService, ProdutoCatalogoDetalhe } from './catalogo-publico.service';
import { CheckoutComponent } from './checkout.component';
import { CheckoutService } from './checkout.service';
import { PedidoConfirmacaoComponent } from './pedido-confirmacao.component';
import { ItemCompra, PedidoCliente } from './pedidos.service';

const produtoId = '20000000-0000-4000-8000-000000000001';
const varianteId = '20000000-0000-4000-8000-000000000002';
const outraVariante = '20000000-0000-4000-8000-000000000003';
const pedidoId = '20000000-0000-4000-8000-000000000004';
const clienteId = '20000000-0000-4000-8000-000000000005';
const chaveLocal = 'xaquiamuni:tentativa-compra:v1';
const chaveCarrinho = 'xaquiamuni:carrinho:v1';
const item: ItemCompra = { produtoId, produtoVarianteId: varianteId, quantidade: 1 };
const imagem = {
  id: 'principal',
  conteudoUrl: '/api/catalogo/produtos/x/imagens/y/conteudo',
  ordemExibicao: 1,
  principal: true,
  opcaoIds: [],
};
const opcao = {
  id: varianteId,
  identificacao: 'Lavanda',
  unidadeVenda: 'UN',
  precoVenda: 2.5,
  principal: true,
  pesoLiquidoGramas: null,
  volumeLiquidoMililitros: null,
  caracteristicas: [],
  disponivel: true,
};
const produto: ProdutoCatalogoDetalhe = {
  id: produtoId,
  nome: 'Sabonete',
  descricao: null,
  categoriaId: 'categoria',
  categoriaNome: 'Sabonetes',
  requerSelecaoDeOpcao: true,
  opcaoPrincipalId: varianteId,
  fotoPrincipal: imagem,
  galeria: [imagem],
  caracteristicas: [],
  disponivel: true,
  opcoes: [opcao, { ...opcao, id: outraVariante, identificacao: 'Outra' }],
};
const pedido: PedidoCliente = {
  id: pedidoId,
  modalidadeEntrega: 'RetiradaLocal',
  situacao: 'AguardandoPagamento',
  criadoEmUtc: '2026-11-18T08:30:00+00:00',
  prazoPagamentoEmUtc: '2026-11-19T08:30:00+00:00',
  totalCentavos: '9223372036854775807',
  itens: [
    {
      ...item,
      nomeProduto: 'Sabonete',
      nomeVariante: 'Lavanda',
      precoUnitarioCentavos: '9223372036854775807',
      subtotalCentavos: '9223372036854775807',
    },
  ],
};

describe('checkout integrado aos contratos HTTP', () => {
  let http: HttpTestingController;
  let carrinho: CarrinhoService;
  let checkout: CheckoutService;
  let sessao: { userId: string } | null;
  let consultarProduto: ReturnType<typeof vi.fn<CatalogoPublicoService['consultarProduto']>>;

  beforeEach(() => {
    localStorage.removeItem(chaveLocal);
    localStorage.removeItem(chaveCarrinho);
    sessao = { userId: clienteId };
    consultarProduto = vi.fn(() => of(produto));
    // O DOM de testes oferece randomUUID, mas não disponibiliza SubtleCrypto.
    if (!window.crypto.subtle) {
      Object.defineProperty(window.crypto, 'subtle', {
        configurable: true,
        value: {
          digest: async (_: unknown, buffer: Uint8Array) => {
            const bytes = new Uint8Array(32);
            bytes.set(buffer.slice(-32));
            return bytes.buffer;
          },
        },
      });
    }
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'checkout', component: CheckoutComponent },
          { path: 'login', component: LoginComponent },
          { path: 'pedidos/:id', component: PedidoConfirmacaoComponent },
        ]),
        {
          provide: AuthService,
          useValue: {
            obterSessao: () => of(sessao),
            login: () => {
              sessao = { userId: clienteId };
              return of(undefined);
            },
          },
        },
        { provide: CatalogoPublicoService, useValue: { consultarProduto } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    carrinho = TestBed.inject(CarrinhoService);
    checkout = TestBed.inject(CheckoutService);
  });

  afterEach(() => {
    http.verify();
    localStorage.removeItem(chaveLocal);
    localStorage.removeItem(chaveCarrinho);
  });

  async function postAposCsrf(token = 'token-de-teste'): Promise<TestRequest> {
    const csrf = http.expectOne('/api/auth/csrf');
    expect(csrf.request.method).toBe('GET');
    csrf.flush({ requestToken: token });
    await vi.waitFor(() => expect(checkout.pendente()).not.toBeNull());
    const post = http.expectOne({ method: 'POST', url: '/api/pedidos' });
    expect(post.request.headers.get('X-XSRF-TOKEN')).toBe(token);
    return post;
  }

  it('cria uma vez, usa apenas os itens e mostra os centavos definitivos ao consultar', async () => {
    carrinho.adicionar(produto, varianteId);
    const harness = await RouterTestingHarness.create('/checkout');
    const view = harness.routeNativeElement!;
    expect(view.textContent).toContain('Retirada local');
    expect(view.textContent).toContain('2,50');
    const botao = view.querySelector<HTMLButtonElement>('button')!;
    expect(botao.type).toBe('button');
    botao.click();
    botao.click();
    const post = await postAposCsrf();
    expect(post.request.body).toEqual({ itens: [item] });
    expect(post.request.headers.get('Idempotency-Key')).toMatch(/^[a-f0-9-]{36}$/i);
    expect(localStorage.getItem(chaveLocal)).not.toContain(clienteId);
    expect(localStorage.getItem(chaveLocal)).not.toContain('token-de-teste');
    expect(carrinho.itens()).toHaveLength(1);
    harness.detectChanges();
    expect(view.querySelector('[role="status"]')?.textContent).toContain('Processando');
    post.flush(pedido, { status: 201, statusText: 'Created' });
    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe(`/pedidos/${pedidoId}`));
    http.expectNone({ method: 'GET', url: `/api/pedidos/${pedidoId}` });
    harness.detectChanges();
    const confirmado = harness.routeNativeElement!;
    expect(confirmado.textContent).toContain('92.233.720.368.547.758,07');
    expect(confirmado.textContent).toContain('Aguardando pagamento');
    expect(confirmado.textContent).toContain(
      'As instruções de pagamento ainda não estão disponíveis.',
    );
    expect(confirmado.querySelector('time')?.getAttribute('datetime')).toBe(
      pedido.prazoPagamentoEmUtc,
    );
    expect(carrinho.itens()).toEqual([]);
    expect(localStorage.getItem(chaveLocal)).toBeNull();
  });

  it('mantém o carrinho no redirecionamento e retorna à revisão após login', async () => {
    sessao = null;
    carrinho.adicionar(produto, varianteId);
    const harness = await RouterTestingHarness.create('/checkout');
    harness.routeNativeElement!.querySelector<HTMLButtonElement>('button')!.click();
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'token' });
    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/login?retorno=checkout'));
    http.expectNone('/api/pedidos');
    expect(carrinho.itens()).toHaveLength(1);
    expect(localStorage.getItem(chaveLocal)).toBeNull();
    const login = harness.routeNativeElement!;
    expect(login.querySelector('a[href="/cadastro?retorno=checkout"]')).not.toBeNull();
    for (const [name, valor] of [
      ['email', 'cliente@example.com'],
      ['senha', 'senha'],
    ]) {
      const input = login.querySelector<HTMLInputElement>(`[formControlName="${name}"]`)!;
      input.value = valor;
      input.dispatchEvent(new Event('input'));
    }
    login.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/checkout'));
    expect(carrinho.itens()).toHaveLength(1);
    http.expectNone('/api/pedidos');
  });

  it('repete o POST original após perda de resposta e restauração, preservando os itens extras', async () => {
    carrinho.adicionar(produto, varianteId);
    const primeiro = checkout.enviar([item], false);
    expect(await checkout.enviar([item], false)).toBeNull();
    const post = await postAposCsrf();
    const chave = post.request.headers.get('Idempotency-Key');
    post.error(new ProgressEvent('error'));
    expect(await primeiro).toMatchObject({ estado: 'erro' });
    expect(carrinho.quantidadeTotal()).toBe(1);
    carrinho.adicionar(produto, varianteId);
    carrinho.adicionar(produto, outraVariante);
    const restaurado = TestBed.runInInjectionContext(() => new CheckoutService());
    expect(restaurado.pendente()?.chave).toBe(chave);
    const carrinhoAtual = [
      { ...item, quantidade: 2 },
      { ...item, produtoVarianteId: outraVariante },
    ];
    expect(await restaurado.enviar(carrinhoAtual, false)).toMatchObject({ estado: 'erro' });
    http.expectNone('/api/pedidos');

    sessao = { userId: '20000000-0000-4000-8000-000000000006' };
    const outraConta = restaurado.enviar(carrinhoAtual, true);
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'outro-token' });
    expect(await outraConta).toMatchObject({
      estado: 'erro',
      mensagem: expect.stringContaining('conta usada'),
    });
    http.expectNone('/api/pedidos');

    sessao = { userId: clienteId };
    const repeticao = restaurado.enviar(carrinhoAtual, true);
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'token-novo' });
    let replay: TestRequest | undefined;
    await vi.waitFor(() => {
      replay = http.expectOne({ method: 'POST', url: '/api/pedidos' });
    });
    expect(replay!.request.headers.get('Idempotency-Key')).toBe(chave);
    expect(replay!.request.body).toEqual({ itens: [item] });
    replay!.flush(pedido, { status: 200, statusText: 'OK' });
    expect(await repeticao).toEqual({ estado: 'sucesso', pedido });
    expect(carrinho.itens().map(({ varianteId, quantidade }) => [varianteId, quantidade])).toEqual([
      [varianteId, 1],
      [outraVariante, 1],
    ]);
    expect(localStorage.getItem(chaveLocal)).toBeNull();
  });

  it.each([
    [409, 'itens_indisponiveis', false, 'Não há estoque'],
    [409, 'chave_em_uso', true, 'entrou em conflito'],
    [403, null, false, 'não está habilitada'],
    [400, null, false, 'solicitação foi recusada'],
    [500, null, true, 'confirmar o resultado'],
  ])(
    'trata o status %i com código %s e preserva o carrinho',
    async (status, codigo, persistir, texto) => {
      carrinho.adicionar(produto, varianteId);
      const tentativa = checkout.enviar([item], false);
      const post = await postAposCsrf();
      post.flush({ codigo }, { status, statusText: 'Recusado' });
      expect(await tentativa).toMatchObject({
        estado: 'erro',
        mensagem: expect.stringContaining(texto),
      });
      expect(carrinho.quantidadeTotal()).toBe(1);
      expect(!!localStorage.getItem(chaveLocal)).toBe(persistir);
    },
  );

  it('mantém a tentativa original quando o POST pede autenticação novamente', async () => {
    carrinho.adicionar(produto, varianteId);
    const tentativa = checkout.enviar([item], false);
    const post = await postAposCsrf();
    post.flush(null, { status: 401, statusText: 'Unauthorized' });
    expect(await tentativa).toEqual({ estado: 'login' });
    expect(carrinho.quantidadeTotal()).toBe(1);
    expect(checkout.pendente()?.chave).toBe(post.request.headers.get('Idempotency-Key'));
  });

  it('impede novo pedido vazio ou com indisponibilidade conhecida', async () => {
    const harness = await RouterTestingHarness.create('/checkout');
    expect(harness.routeNativeElement!.textContent).toContain('Seu carrinho está vazio.');
    expect(harness.routeNativeElement!.querySelector('button')).toBeNull();
    carrinho.adicionar(produto, varianteId);
    consultarProduto.mockReturnValue(of({ ...produto, opcoes: [{ ...opcao, disponivel: false }] }));
    await harness.navigateByUrl('/login');
    await harness.navigateByUrl('/checkout');
    expect(harness.routeNativeElement!.querySelector('button')?.disabled).toBe(true);
    expect(harness.routeNativeElement!.textContent).toContain('Opção indisponível.');
    http.expectNone('/api/pedidos');
  });

  it('aplica a retirada dos itens no carrinho apenas uma vez mesmo após recarregar', () => {
    carrinho.adicionar(produto, varianteId);
    carrinho.adicionar(produto, varianteId);
    const chave = crypto.randomUUID();
    expect(carrinho.confirmarCompra(chave, [item])).toBe(true);
    expect(carrinho.quantidadeTotal()).toBe(1);
    const restaurado = TestBed.runInInjectionContext(() => new CarrinhoService());
    expect(restaurado.confirmarCompra(chave, [item])).toBe(true);
    expect(restaurado.quantidadeTotal()).toBe(1);
  });

  it('consulta a confirmação diretamente e mantém a precisão de centavos', async () => {
    const harness = await RouterTestingHarness.create(`/pedidos/${pedidoId}`);
    expect(harness.routeNativeElement!.querySelector('[role=status]')).not.toBeNull();
    http.expectOne({ method: 'GET', url: `/api/pedidos/${pedidoId}` }).flush(pedido);
    harness.detectChanges();
    expect(harness.routeNativeElement!.textContent).toContain(pedidoId);
    expect(
      harness.routeNativeElement!.querySelector('ul[aria-label="Itens do pedido"]'),
    ).not.toBeNull();
    expect(formatarCentavos('9007199254740993')).toContain('90.071.992.547.409,93');
  });
});
