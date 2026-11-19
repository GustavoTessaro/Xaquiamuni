import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { AdminAuthService } from '../../../core/admin-auth/admin-auth.service';
import { ProdutosService } from '../../../core/admin-produtos/produtos.service';
import {
  ContagemProdutoAcabadoRequest,
  EntradaProdutoAcabadoRequest,
  LoteProdutoAcabado,
  PoliticaEstoqueProdutoAcabado,
  PosicaoProdutoAcabado,
  ProdutosAcabadosService,
} from '../../../core/admin-estoque/produtos-acabados.service';
import { AdminProdutosAcabadosComponent } from './admin-produtos-acabados.component';

const loteInicial: LoteProdutoAcabado = {
  loteId: 'lot-1',
  codigo: 'PA-20261117-ABC',
  dataFabricacao: '2026-11-17',
  dataValidade: '2026-12-17',
  origemValidade: 'PrazoPadrao',
  prazoValidadeAplicadoDias: 30,
  valido: true,
  temRegistroSaldo: true,
  versaoSaldo: 'AQIDBAUG',
  quantidadeVendavel: 5,
  quantidadeDanificada: 1,
  quantidadeQuarentena: 0,
  saldoFisico: 6,
  saldoUtilizavel: 5,
  saldoReservado: 0,
  saldoDisponivel: 5,
};

const produto = {
  id: 'product-1',
  nome: 'Sabonete',
  descricao: null,
  categoriaId: 'category-1',
  categoriaNome: 'Corpo',
  categoriaAtiva: true,
  publicado: true,
  quantidadeImagens: 0,
  fotoPrincipalUrl: null,
  variantes: [
    {
      id: 'variant-1',
      identificacao: 'Lavanda 100 g',
      unidadeVenda: 'UN',
      precoVenda: 25,
      ativa: true,
      padrao: true,
      pesoLiquidoGramas: 100,
      volumeLiquidoMililitros: null,
      caracteristicas: [],
    },
  ],
  caracteristicas: [],
};

function criarPosicao(lote = loteInicial): PosicaoProdutoAcabado {
  return {
    produtoVarianteId: 'variant-1',
    varianteAtiva: true,
    saldoFisico: lote.saldoFisico,
    saldoUtilizavel: lote.saldoUtilizavel,
    saldoReservado: lote.saldoReservado,
    saldoDisponivel: lote.saldoDisponivel,
    estoqueMinimo: 2,
    estoqueMinimoConfigurado: true,
    situacao: 'Normal',
    lotes: [lote],
  };
}

function criarApi() {
  return {
    configurarPolitica: vi.fn(),
    consultarPosicao: vi.fn(() => of(criarPosicao())),
    consultarLote: vi.fn(() => of(loteInicial)),
    consultarPolitica: vi.fn(() =>
      of({
        produtoVarianteId: 'variant-1',
        estoqueMinimo: 2,
        prazoValidadePadraoDias: 30,
      }),
    ),
    listarMovimentacoes: vi.fn<ProdutosAcabadosService['listarMovimentacoes']>(() =>
      of({ itens: [], totalItens: 0, pagina: 1, itensPorPagina: 20 }),
    ),
    registrarEntrada: vi.fn(
      (_varianteId: string, _request: EntradaProdutoAcabadoRequest, _key: string) =>
        of({ id: 'movement-1' }),
    ),
    registrarSaida: vi.fn(() => of({ id: 'movement-2' })),
    registrarSaidaAutomatica: vi.fn(() => of({ movimentoIds: ['movement-2'], repetida: false })),
    transferir: vi.fn(() => of({ id: 'movement-3' })),
    registrarContagem: vi.fn(
      (_varianteId: string, _request: ContagemProdutoAcabadoRequest, _key: string) =>
        of({ semDiferenca: false, movimentos: [], repetida: false }),
    ),
    estornarMovimento: vi.fn(() => of({ id: 'movement-4' })),
  };
}

async function montarComponente(
  api: ReturnType<typeof criarApi>,
  atorId = 'actor-1',
  varianteSelecionada = true,
  listar = () => of([produto]),
  produtoId?: string,
): Promise<ComponentFixture<AdminProdutosAcabadosComponent>> {
  const params = convertToParamMap(
    varianteSelecionada ? { varianteId: 'variant-1' } : { produtoId },
  );
  await TestBed.configureTestingModule({
    imports: [AdminProdutosAcabadosComponent],
    providers: [
      provideRouter([]),
      {
        provide: ActivatedRoute,
        useValue: { snapshot: { paramMap: params }, paramMap: of(params) },
      },
      {
        provide: AdminAuthService,
        useValue: { getCurrentSession: () => of({ userId: atorId }) },
      },
      { provide: ProdutosService, useValue: { listar } },
      { provide: ProdutosAcabadosService, useValue: api },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(AdminProdutosAcabadosComponent);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  return fixture;
}

it('restringe a seleção ao produto da URL e abre a variante mantendo os filtros', async () => {
  const outro = { ...produto, id: 'outro-produto', nome: 'Outro produto' };
  const alvo = {
    ...produto,
    variantes: [
      ...produto.variantes,
      { ...produto.variantes[0], id: 'variant-2', identificacao: 'Lavanda 200 g' },
    ],
  };
  const api = criarApi();
  const fixture = await montarComponente(
    api,
    'actor-1',
    false,
    () => of([alvo, outro]),
    produto.id,
  );
  const root = fixture.nativeElement as HTMLElement;
  expect(root.textContent).toContain('Lavanda 100 g');
  expect(root.textContent).not.toContain('Outro produto');
  expect(root.querySelector('main a[href="/admin/produtos"]')?.textContent).toContain(
    'Voltar para Produtos',
  );
  expect(api.consultarPosicao).not.toHaveBeenCalled();
  const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  clicar(fixture, 'Lavanda 200 g');
  expect(navigate).toHaveBeenCalledWith(['/admin/estoque/produtos-acabados', 'variant-2'], {
    queryParamsHandling: 'preserve',
  });
});

function clicar(fixture: ComponentFixture<AdminProdutosAcabadosComponent>, texto: string): void {
  const root = fixture.nativeElement as HTMLElement;
  const button = Array.from(root.querySelectorAll<HTMLButtonElement>('button')).find((item) =>
    item.textContent?.includes(texto),
  );
  if (!button) {
    throw new Error(`Botão não encontrado: ${texto}`);
  }
  button.click();
  fixture.detectChanges();
}

function preencher(
  fixture: ComponentFixture<AdminProdutosAcabadosComponent>,
  selector: string,
  value: string,
): void {
  const root = fixture.nativeElement as HTMLElement;
  const input = root.querySelector<HTMLInputElement>(selector);
  if (!input) {
    throw new Error(`Campo não encontrado: ${selector}`);
  }
  input.value = value;
  input.dispatchEvent(new Event('input'));
  input.dispatchEvent(new Event('change'));
  fixture.detectChanges();
}

async function estabilizar(
  fixture: ComponentFixture<AdminProdutosAcabadosComponent>,
): Promise<void> {
  await vi.waitFor(
    async () => {
      await fixture.whenStable();
      fixture.detectChanges();
      expect(fixture.nativeElement.textContent).not.toMatch(/Confirmando\.\.\.|Registrando\.\.\./);
    },
    { timeout: 3000, interval: 10 },
  );
}

function preencherEntrada(
  fixture: ComponentFixture<AdminProdutosAcabadosComponent>,
  quantidade: string,
): void {
  clicar(fixture, 'Registrar entrada');
  preencher(fixture, '#finished-entry-quantity', quantidade);
  preencher(fixture, '#finished-entry-fabrication', '2026-11-17');
  preencher(fixture, '#finished-entry-reason', 'Recebimento reservado');
}

function abrirContagem(fixture: ComponentFixture<AdminProdutosAcabadosComponent>): void {
  clicar(fixture, 'Conferir estoque');
}

function preencherContagem(
  fixture: ComponentFixture<AdminProdutosAcabadosComponent>,
  vendavel: string,
  danificada: string,
  quarentena: string,
  motivo = 'Contagem física',
): void {
  preencher(fixture, '#count-vendavel', vendavel);
  preencher(fixture, '#count-damaged', danificada);
  preencher(fixture, '#count-quarantine', quarentena);
  preencher(fixture, '#count-reason', motivo);
}

function clicarConferirEstoque(fixture: ComponentFixture<AdminProdutosAcabadosComponent>): void {
  abrirContagem(fixture);
  preencherContagem(fixture, '4', '2', '0');
  clicar(fixture, 'Revisar contagem');
}

describe('AdminProdutosAcabadosComponent', () => {
  beforeEach(() => sessionStorage.clear());

  afterEach(() => {
    sessionStorage.clear();
    TestBed.resetTestingModule();
  });

  it('exibe seleção de produto e variante quando nenhuma variante foi escolhida', async () => {
    const fixture = await montarComponente(criarApi(), 'actor-1', false);
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Selecionar produto e variante');
    expect(text).toContain('Sabonete');
    expect(text).toContain('Lavanda 100 g');
  });

  it('distingue falha do catálogo e repete somente a listagem, sem duplicar a tentativa', async () => {
    const resposta = new Subject<(typeof produto)[]>();
    const listar = vi
      .fn(() => resposta.asObservable())
      .mockImplementationOnce(() => throwError(() => new HttpErrorResponse({ status: 503 })));
    const api = criarApi();
    const fixture = await montarComponente(api, 'actor-1', false, listar);
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector('[role="alert"]')?.textContent).toContain(
      'Não foi possível carregar os produtos.',
    );
    expect(root.textContent).not.toContain('Nenhum produto cadastrado');
    const tentar = Array.from(root.querySelectorAll('button')).find(
      (button) => button.textContent?.trim() === 'Tentar novamente',
    )!;
    tentar.click();
    tentar.click();
    fixture.detectChanges();

    expect(listar).toHaveBeenCalledTimes(2);
    expect(root.querySelector('[role="status"]')?.textContent).toContain('Carregando produtos');
    expect(root.textContent).not.toContain('Nenhum produto cadastrado');

    resposta.next([produto]);
    resposta.complete();
    fixture.detectChanges();

    expect(root.textContent).toContain('Lavanda 100 g');
    expect(root.querySelector('[role="alert"]')).toBeNull();
    expect(api.consultarPosicao).not.toHaveBeenCalled();
    expect(api.consultarPolitica).not.toHaveBeenCalled();
  });

  it('mostra a lista vazia somente após uma consulta bem-sucedida', async () => {
    const fixture = await montarComponente(criarApi(), 'actor-1', false, () => of([]));
    expect(fixture.nativeElement.textContent).toContain('Nenhum produto cadastrado');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });

  it('preserva a variante do endereço quando a consulta inicial do catálogo falha', async () => {
    const listar = vi
      .fn(() => of([produto]))
      .mockImplementationOnce(() => throwError(() => new HttpErrorResponse({ status: 503 })));
    const api = criarApi();
    const fixture = await montarComponente(api, 'actor-1', true, listar);
    expect(api.consultarPosicao).not.toHaveBeenCalled();

    clicar(fixture, 'Tentar novamente');

    expect(listar).toHaveBeenCalledTimes(2);
    expect(api.consultarPosicao).toHaveBeenCalledExactlyOnceWith('variant-1');
    expect(fixture.nativeElement.textContent).toContain('Lavanda 100 g');
    expect(fixture.nativeElement.textContent).toContain('Resumo do estoque');
  });

  it('recupera a posição inicial da mesma variante e reaproveita a política carregada', async () => {
    const api = criarApi();
    api.consultarPosicao.mockImplementationOnce(() =>
      throwError(() => new HttpErrorResponse({ status: 503 })),
    );
    const listar = vi.fn(() => of([produto]));
    const fixture = await montarComponente(api, 'actor-1', true, listar);
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector('[role="alert"]')?.textContent).toContain(
      'Não foi possível consultar o estoque desta variante.',
    );
    expect(root.textContent).not.toContain('Resumo do estoque');
    expect(root.textContent).not.toContain('Não foi possível consultar a política');
    expect(api.listarMovimentacoes).not.toHaveBeenCalled();

    clicar(fixture, 'Tentar consultar o estoque novamente');

    expect(root.textContent).toContain('Resumo do estoque');
    expect(root.querySelector<HTMLInputElement>('#finished-expiry')?.value).toBe('30');
    expect(root.querySelector('[role="alert"]')).toBeNull();
    expect(api.consultarPosicao.mock.calls).toEqual([['variant-1'], ['variant-1']]);
    expect(api.consultarPolitica).toHaveBeenCalledTimes(1);
    expect(api.listarMovimentacoes).toHaveBeenCalledTimes(1);
    expect(listar).toHaveBeenCalledTimes(1);
  });

  it('mantém posição e histórico disponíveis quando apenas a política falha', async () => {
    const api = criarApi();
    api.consultarPolitica.mockImplementationOnce(() =>
      throwError(() => new HttpErrorResponse({ status: 503 })),
    );
    const fixture = await montarComponente(api);
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector('[role="alert"]')?.textContent).toContain(
      'Não foi possível consultar a política de estoque.',
    );
    expect(root.textContent).toContain('Resumo do estoque');
    expect(root.textContent).toContain('Histórico');
    expect(root.textContent).not.toContain('Salvar política');

    clicar(fixture, 'Tentar consultar a política novamente');

    expect(root.querySelector('[role="alert"]')).toBeNull();
    expect(root.querySelector<HTMLInputElement>('#finished-expiry')?.value).toBe('30');
    expect(api.consultarPolitica).toHaveBeenCalledTimes(2);
    expect(api.consultarPosicao).toHaveBeenCalledTimes(1);
    expect(api.listarMovimentacoes).toHaveBeenCalledTimes(1);
  });

  it('associa rótulos e ajuda aos campos da política e só apresenta erros após interação', async () => {
    const fixture = await montarComponente(criarApi());
    const root = fixture.nativeElement as HTMLElement;
    const minimo = root.querySelector<HTMLInputElement>('#finished-minimum')!;
    const prazo = root.querySelector<HTMLInputElement>('#finished-expiry')!;

    expect(minimo.labels?.[0]?.textContent?.trim()).toBe('Estoque mínimo');
    expect(prazo.labels?.[0]?.textContent?.trim()).toBe('Prazo padrão de validade (dias)');
    expect(minimo.getAttribute('aria-describedby')).toBe('finished-minimum-help');
    expect(prazo.getAttribute('aria-describedby')).toBe('finished-expiry-help');
    expect(root.querySelector('#finished-minimum-help')?.textContent).toContain(
      'Em branco, o estoque mínimo fica sem configuração.',
    );
    expect(root.querySelector('#finished-expiry-help')?.textContent).toContain(
      'Sem prazo padrão, informe a validade ao criar o lote.',
    );
    preencher(fixture, '#finished-minimum', '-1');
    expect(minimo.hasAttribute('aria-invalid')).toBe(false);
    expect(root.querySelector('#finished-minimum-error')).toBeNull();

    minimo.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    expect(minimo.getAttribute('aria-invalid')).toBe('true');
    expect(minimo.getAttribute('aria-describedby')).toBe(
      'finished-minimum-error finished-minimum-help',
    );
    expect(root.querySelector('#finished-minimum-error')?.textContent).toContain(
      'Erro: Informe um inteiro maior ou igual a zero.',
    );
    expect(prazo.hasAttribute('aria-invalid')).toBe(false);

    preencher(fixture, '#finished-minimum', '0');
    expect(minimo.hasAttribute('aria-invalid')).toBe(false);
    expect(minimo.getAttribute('aria-describedby')).toBe('finished-minimum-help');
    expect(root.querySelector('#finished-minimum-error')).toBeNull();
  });

  it('foca o primeiro erro ao salvar e envia zero e prazo em branco depois da correção', async () => {
    const api = criarApi();
    const resposta = new Subject<PoliticaEstoqueProdutoAcabado>();
    api.configurarPolitica.mockReturnValue(resposta);
    const fixture = await montarComponente(api);
    const root = fixture.nativeElement as HTMLElement;
    const minimo = root.querySelector('#finished-minimum');
    const prazo = root.querySelector('#finished-expiry');
    preencher(fixture, '#finished-minimum', '-1');
    preencher(fixture, '#finished-expiry', '1.5');

    clicar(fixture, 'Salvar política');
    expect(api.configurarPolitica).not.toHaveBeenCalled();
    expect(root.ownerDocument.activeElement).toBe(minimo);
    expect(prazo?.getAttribute('aria-invalid')).toBe('true');
    expect(prazo?.getAttribute('aria-describedby')).toBe(
      'finished-expiry-error finished-expiry-help',
    );
    expect(root.querySelector('#finished-expiry-error')?.textContent).toContain(
      'Erro: Informe um inteiro maior ou igual a zero.',
    );

    preencher(fixture, '#finished-minimum', '0');
    clicar(fixture, 'Salvar política');
    expect(root.ownerDocument.activeElement).toBe(prazo);
    expect(api.configurarPolitica).not.toHaveBeenCalled();

    preencher(fixture, '#finished-expiry', '');
    expect(prazo?.hasAttribute('aria-invalid')).toBe(false);
    expect(prazo?.getAttribute('aria-describedby')).toBe('finished-expiry-help');
    expect(root.querySelector('#finished-expiry-error')).toBeNull();
    clicar(fixture, 'Salvar política');
    expect(api.configurarPolitica).toHaveBeenCalledExactlyOnceWith('variant-1', {
      estoqueMinimo: 0,
      prazoValidadePadraoDias: null,
    });
    const salvar = Array.from(root.querySelectorAll('button')).find((button) =>
      button.textContent?.includes('Salvando...'),
    );
    expect(salvar?.disabled).toBe(true);

    resposta.next({
      produtoVarianteId: 'variant-1',
      estoqueMinimo: 0,
      prazoValidadePadraoDias: null,
    });
    resposta.complete();
    fixture.detectChanges();
    expect(root.querySelector('[role="status"]')?.textContent).toContain(
      'Política de estoque salva.',
    );
    expect(salvar?.disabled).toBe(false);
  });

  it('prioriza os saldos e recolhe detalhes, lotes e configurações', async () => {
    const fixture = await montarComponente(criarApi());
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('main h1')?.textContent?.trim()).toBe(produto.nome);
    expect(root.textContent).toContain('Lavanda 100 g');
    const saldo = root.querySelector('[aria-labelledby="finished-position-title"]')!;
    expect(
      Array.from(saldo.querySelectorAll('dl:first-of-type dt'))
        .slice(0, 3)
        .map((dt) => dt.textContent?.trim()),
    ).toEqual(['Disponível para venda', 'Reservado em pedidos', 'Total físico']);
    expect(saldo.querySelector('details')?.open).toBe(false);
    expect(saldo.textContent).toContain('A condição vendável pode incluir lotes vencidos.');
    for (const id of ['finished-lots-title', 'finished-policy-title']) {
      expect(root.querySelector<HTMLDetailsElement>('#' + id)?.closest('details')?.open).toBe(
        false,
      );
    }
  });

  it('separa a quantidade na condição vendável do saldo disponível quando há lote vencido', async () => {
    const api = criarApi();
    const loteVencido: LoteProdutoAcabado = {
      ...loteInicial,
      loteId: 'lot-vencido',
      codigo: 'PA-20261001-VENCIDO',
      dataFabricacao: '2026-10-01',
      dataValidade: '2026-10-31',
      valido: false,
      quantidadeVendavel: 3,
      quantidadeDanificada: 0,
      quantidadeQuarentena: 2,
      saldoFisico: 5,
      saldoUtilizavel: 0,
      saldoReservado: 2,
      saldoDisponivel: 0,
    };
    api.consultarPosicao.mockReturnValueOnce(
      of({
        ...criarPosicao(),
        saldoFisico: 11,
        saldoReservado: 3,
        saldoDisponivel: 4,
        lotes: [{ ...loteInicial, saldoReservado: 1, saldoDisponivel: 4 }, loteVencido],
      }),
    );

    const fixture = await montarComponente(api);
    const root = fixture.nativeElement as HTMLElement;
    const indicadores = new Map(
      Array.from(
        root.querySelectorAll('[aria-labelledby="finished-position-title"] dl dt'),
        (label) => [label.textContent?.trim(), label.nextElementSibling?.textContent?.trim()],
      ),
    );
    expect(indicadores).toEqual(
      new Map([
        ['Total físico', '11 un.'],
        ['Estoque vendável', '5 un.'],
        ['Reservado em pedidos', '3 un.'],
        ['Disponível para venda', '4 un.'],
        ['Na condição vendável', '8 un.'],
        ['Avariado (danificado)', '1 un.'],
        ['Quarentena', '2 un.'],
      ]),
    );
    const lotes = root.querySelector('#finished-lots-title')!.closest('details')!;
    const cartaoVencido = Array.from(lotes.querySelectorAll('li')).find((item) =>
      item.textContent?.includes(loteVencido.codigo),
    )!;
    const indicadoresVencido = new Map(
      Array.from(cartaoVencido.querySelectorAll('dt'), (label) => [
        label.textContent?.trim(),
        label.nextElementSibling?.textContent?.trim(),
      ]),
    );
    expect(indicadoresVencido.get('Comprometido por reservas')).toBe('2 un.');
    expect(indicadoresVencido.get('Disponível para venda')).toBe('0 un.');
    expect(cartaoVencido.textContent).toContain('estoque vendável deste lote é insuficiente');
  });

  it('exibe uma movimentação datada e permite abrir a correção pelo histórico', async () => {
    const api = criarApi();
    api.listarMovimentacoes.mockReturnValueOnce(
      of({
        itens: [
          {
            id: 'mov-1',
            tipo: 'EntradaManual',
            quantidade: 2,
            condicaoOrigem: null,
            condicaoDestino: 'Vendavel',
            quantidadeContada: null,
            saldoAnteriorVendavel: 0,
            saldoAnteriorDanificada: 0,
            saldoAnteriorQuarentena: 0,
            saldoPosteriorVendavel: 2,
            saldoPosteriorDanificada: 0,
            saldoPosteriorQuarentena: 0,
            motivo: 'Entrada de teste',
            observacao: null,
            responsavelId: 'actor-1',
            registradoEmUtc: '2026-11-18T12:00:00Z',
            loteId: 'lot-1',
            codigoLote: loteInicial.codigo,
            movimentoOriginalId: null,
          },
        ],
        totalItens: 1,
        pagina: 1,
        itensPorPagina: 20,
      }),
    );
    const fixture = await montarComponente(api);
    const root = fixture.nativeElement as HTMLElement;
    const historico = root.querySelector('[aria-labelledby="finished-history-title"]')!;
    expect(historico.textContent).toContain('18/11/2026');
    expect(historico.textContent).toContain('Entrada de teste');
    const detalhes = historico.querySelector('details')!;
    expect(detalhes.open).toBe(false);
    detalhes.open = true;
    clicar(fixture, 'Corrigir movimentação');
    expect(root.querySelectorAll('main form')).toHaveLength(1);
    expect(root.querySelector('#reverse-reason')).not.toBeNull();
    expect(api.estornarMovimento).not.toHaveBeenCalled();
  });

  it('apresenta o estado vazio ao abrir conferência e mantém a entrada disponível', async () => {
    const api = criarApi();
    api.consultarPosicao.mockReturnValueOnce(
      of({
        ...criarPosicao(),
        lotes: [],
        saldoFisico: 0,
        saldoUtilizavel: 0,
        saldoDisponivel: 0,
      }),
    );
    const fixture = await montarComponente(api);
    const root = fixture.nativeElement as HTMLElement;
    const lotes = root.querySelector('#finished-lots-title')!.closest('details')!;
    lotes.open = true;
    expect(lotes.textContent).toContain('Nenhum lote possui posição registrada');
    expect(lotes.querySelector('button')).toBeNull();
    expect(root.querySelector('#finished-routine-actions-title')?.textContent).toContain(
      'Registrar entrada',
    );
  });

  it('identifica a falta de saldo para venda quando o físico está todo comprometido', async () => {
    const api = criarApi();
    api.consultarPosicao.mockReturnValueOnce(
      of({
        ...criarPosicao({ ...loteInicial, saldoReservado: 5, saldoDisponivel: 0 }),
        situacao: 'SemEstoque',
      }),
    );
    const fixture = await montarComponente(api);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain(
      'Sem saldo disponível para venda',
    );
  });

  it('mantém as ações secundárias acessíveis e mostra um formulário por vez', async () => {
    const fixture = await montarComponente(criarApi());
    const root = fixture.nativeElement as HTMLElement;
    const rotina = root.querySelector('#finished-routine-actions-title');
    expect(rotina?.textContent).toContain('Registrar entrada');
    expect(rotina?.textContent).toContain('Registrar saída');
    const outras = root.querySelector('#finished-exception-actions-title')?.closest('details');
    expect(outras?.open).toBe(false);
    outras!.open = true;
    expect(outras?.textContent).toContain('Registrar descarte');
    expect(outras?.textContent).toContain('Mudar condição do lote');
    clicar(fixture, 'Registrar perda');
    expect(root.querySelectorAll('main form')).toHaveLength(1);
    expect(root.querySelector('#finished-routine-actions-title')).toBeNull();
    clicar(fixture, 'Cancelar operação');
    expect(root.querySelectorAll('main form')).toHaveLength(0);
    expect(root.querySelector('#finished-routine-actions-title')).not.toBeNull();
  });

  it('registra saída sem selecionar lote e atualiza a posição e o histórico', async () => {
    const api = criarApi();
    const fixture = await montarComponente(api);
    const root = fixture.nativeElement as HTMLElement;

    clicar(fixture, 'Registrar saída');
    expect(root.querySelector('#finished-operation-lot')).toBeNull();
    expect(root.querySelector('#finished-auto-available')?.textContent).toContain('5');
    preencher(fixture, '#finished-auto-quantity', '2');
    preencher(fixture, '#finished-auto-reason', 'Uso interno');
    clicar(fixture, 'Confirmar saída');

    await vi.waitFor(() => expect(api.registrarSaidaAutomatica).toHaveBeenCalledTimes(1));
    expect(api.registrarSaidaAutomatica).toHaveBeenCalledWith(
      'variant-1',
      { quantidade: 2, motivo: 'Uso interno' },
      expect.any(String),
    );
    expect(api.registrarSaida).not.toHaveBeenCalled();
    expect(api.consultarPosicao).toHaveBeenCalledTimes(2);
    expect(api.listarMovimentacoes).toHaveBeenCalledTimes(2);
  });

  it('identifica lotes por datas e saldo nos seletores de movimentação e histórico', async () => {
    const fixture = await montarComponente(criarApi());
    const root = fixture.nativeElement as HTMLElement;

    const filtroHistorico = root.querySelector<HTMLOptionElement>(
      '#finished-history-lot option[value="lot-1"]',
    );
    expect(filtroHistorico?.textContent).toContain('Validade 17/12/2026');
    expect(filtroHistorico?.textContent).toContain('fabricação 17/11/2026');
    expect(filtroHistorico?.textContent).toContain(loteInicial.codigo);

    clicar(fixture, 'Registrar entrada');
    const modoLote = root.querySelector<HTMLSelectElement>('#finished-entry-lot-mode');
    expect(modoLote).not.toBeNull();
    modoLote!.value = 'existente';
    modoLote!.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();

    const entradaOption = root.querySelector<HTMLOptionElement>(
      '#finished-entry-existing-lot option[value="lot-1"]',
    );
    expect(entradaOption?.textContent).toContain('Validade 17/12/2026');
    expect(entradaOption?.textContent).toContain('Válido');
    expect(entradaOption?.textContent).toContain(loteInicial.codigo);

    clicar(fixture, 'Cancelar operação');
    root.querySelector('#finished-exception-actions-title')!.closest('details')!.open = true;
    clicar(fixture, 'Registrar perda');
    const saidaOption = root.querySelector<HTMLOptionElement>(
      '#finished-operation-lot option[value="lot-1"]',
    );
    expect(saidaOption?.textContent).toContain('Fabricação 17/11/2026');
    expect(saidaOption?.textContent).toContain('físico 6 un.');
    expect(saidaOption?.textContent).toContain(loteInicial.codigo);

    clicar(fixture, 'Cancelar operação');
    root.querySelector('#finished-exception-actions-title')!.closest('details')!.open = true;
    clicar(fixture, 'Mudar condição do lote');
    const transferenciaOption = root.querySelector<HTMLOptionElement>(
      '#finished-transfer-lot option[value="lot-1"]',
    );
    expect(transferenciaOption?.textContent).toContain('Validade 17/12/2026');
    expect(transferenciaOption?.textContent).toContain(loteInicial.codigo);
  });

  it('abre o formulário no cartão do lote com quantidades encontradas vazias e foco acessível', async () => {
    const api = criarApi();
    const fixture = await montarComponente(api);
    const root = fixture.nativeElement as HTMLElement;

    abrirContagem(fixture);

    const form = root.querySelector<HTMLFormElement>('#count-form');
    const card = form?.closest('li');
    const codigoLote = card?.querySelector('details code');
    expect(form).not.toBeNull();
    expect(card?.querySelector('h3')?.textContent).toContain('Validade');
    expect(root.querySelector('#finished-lots-title')?.parentElement?.textContent).toContain(
      'Sabonete · Lavanda 100 g',
    );
    expect(card?.textContent).toContain('17/12/2026');
    expect(card?.textContent).toContain('Fabricação 17/11/2026');
    expect(card?.textContent).toContain('Vendável');
    expect(card?.textContent).toContain('6 un.');
    expect(card?.querySelector('details summary')?.textContent).toContain(
      'Código do lote (rastreabilidade)',
    );
    expect(codigoLote?.textContent).toContain(loteInicial.codigo);
    expect(codigoLote?.getAttribute('title')).toContain(loteInicial.codigo);
    expect(card?.className).toContain('lg:col-span-2');
    expect(root.querySelectorAll('#count-form')).toHaveLength(1);
    expect(root.querySelector<HTMLInputElement>('#count-vendavel')?.value).toBe('');
    expect(root.querySelector<HTMLInputElement>('#count-damaged')?.value).toBe('');
    expect(root.querySelector<HTMLInputElement>('#count-quarantine')?.value).toBe('');
    expect(root.querySelector('#count-vendavel-registered')?.textContent).toContain('5 un.');
    expect(root.querySelector('#count-damaged-registered')?.textContent).toContain('1 un.');
    expect(root.querySelector('#count-quarantine-registered')?.textContent).toContain('0 un.');
    expect(root.querySelectorAll('button[aria-label^="Conferir estoque do lote"]')).toHaveLength(1);
    expect(document.activeElement).toBe(root.querySelector('#count-vendavel'));
    expect(api.registrarContagem).not.toHaveBeenCalled();
  });

  it('mantém somente uma contagem aberta e impede perder valores ao escolher outro lote', async () => {
    const api = criarApi();
    const segundoLote = {
      ...loteInicial,
      loteId: 'lot-2',
      codigo: 'PA-20261117-DEF',
      versaoSaldo: 'BwgJCgsM',
    };
    api.consultarPosicao.mockReturnValue(
      of({
        ...criarPosicao(),
        lotes: [loteInicial, segundoLote],
      }),
    );
    const fixture = await montarComponente(api);
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelectorAll('button[aria-label^="Conferir estoque do lote"]')).toHaveLength(2);
    abrirContagem(fixture);
    preencher(fixture, '#count-vendavel', '4');
    const segundoLoteButton = root.querySelector<HTMLButtonElement>(
      '[aria-label="Conferir estoque do lote PA-20261117-DEF"]',
    );
    expect(segundoLoteButton).not.toBeNull();
    segundoLoteButton!.click();
    fixture.detectChanges();

    expect(root.querySelectorAll('#count-form')).toHaveLength(1);
    expect(root.querySelector('#count-form')?.closest('li')?.textContent).toContain(
      loteInicial.codigo,
    );
    expect(root.querySelector<HTMLInputElement>('#count-vendavel')?.value).toBe('4');
    expect(root.querySelector('[role="alert"]')?.textContent).toContain(
      'Cancele a conferência aberta antes de iniciar a contagem de outro lote.',
    );
  });

  it('exige zeros explícitos e não envia a contagem antes da confirmação final', async () => {
    const api = criarApi();
    const fixture = await montarComponente(api);
    const root = fixture.nativeElement as HTMLElement;

    abrirContagem(fixture);
    preencherContagem(fixture, '0', '0', '0');
    clicar(fixture, 'Revisar contagem');

    expect(root.querySelector('#count-summary-title')).not.toBeNull();
    expect(document.activeElement).toBe(root.querySelector('#count-summary-title'));
    expect(api.registrarContagem).not.toHaveBeenCalled();

    clicar(fixture, 'Confirmar contagem');
    await estabilizar(fixture);

    expect(api.registrarContagem.mock.calls[0]?.[1]).toMatchObject({
      quantidadeVendavel: 0,
      quantidadeDanificada: 0,
      quantidadeQuarentena: 0,
      motivo: 'Contagem física',
    });
  });

  it('revisa diferenças positivas, negativas e nulas antes do envio', async () => {
    const api = criarApi();
    const fixture = await montarComponente(api);
    const root = fixture.nativeElement as HTMLElement;

    abrirContagem(fixture);
    preencherContagem(fixture, '6', '0', '0');
    clicar(fixture, 'Revisar contagem');

    expect(root.querySelector('#count-summary-vendavel-registered')?.textContent).toContain(
      '5 un.',
    );
    expect(root.querySelector('#count-summary-vendavel-found')?.textContent).toContain('6 un.');
    expect(root.querySelector('#count-summary-vendavel-difference')?.textContent).toContain(
      '+1 un.',
    );
    expect(root.querySelector('#count-summary-damaged-registered')?.textContent).toContain('1 un.');
    expect(root.querySelector('#count-summary-damaged-found')?.textContent).toContain('0 un.');
    expect(root.querySelector('#count-summary-damaged-difference')?.textContent).toContain(
      '-1 un.',
    );
    expect(root.querySelector('#count-summary-quarantine-registered')?.textContent).toContain(
      '0 un.',
    );
    expect(root.querySelector('#count-summary-quarantine-found')?.textContent).toContain('0 un.');
    expect(root.querySelector('#count-summary-quarantine-difference')?.textContent).toContain(
      '0 un.',
    );
    const texto = root
      .querySelector('#count-summary-title')
      ?.parentElement?.textContent?.replace(/\s+/g, ' ');
    expect(texto).toContain('Registrado no sistema');
    expect(texto).toContain('Encontrado na contagem');
    expect(texto).toContain('Diferença física total: 0 un.');
    expect(texto).toContain('os ajustes serão registrados no histórico');
    expect(texto).toContain('sem movimentação de ajuste');
    expect(api.registrarContagem).not.toHaveBeenCalled();
  });

  it('preserva o snapshot capturado mesmo se a posição geral for atualizada durante a edição', async () => {
    const api = criarApi();
    const posicaoAtualizada = {
      ...criarPosicao({
        ...loteInicial,
        versaoSaldo: 'BwgJCgsM',
        quantidadeVendavel: 8,
        quantidadeDanificada: 0,
        saldoFisico: 8,
        saldoUtilizavel: 8,
        saldoDisponivel: 8,
      }),
    };
    const fixture = await montarComponente(api);
    const root = fixture.nativeElement as HTMLElement;

    abrirContagem(fixture);
    api.consultarPosicao.mockReturnValueOnce(of(posicaoAtualizada));
    clicar(fixture, 'Atualizar posição');
    await fixture.whenStable();
    fixture.detectChanges();

    preencherContagem(fixture, '6', '0', '0');
    clicar(fixture, 'Revisar contagem');
    expect(root.querySelector('#count-summary-vendavel-registered')?.textContent).toContain(
      '5 un.',
    );
    expect(root.querySelector('#count-summary-vendavel-found')?.textContent).toContain('6 un.');
    expect(root.querySelector('#count-summary-vendavel-difference')?.textContent).toContain(
      '+1 un.',
    );

    clicar(fixture, 'Confirmar contagem');
    await estabilizar(fixture);
    expect(api.registrarContagem.mock.calls[0]?.[1]?.versaoSaldoEsperada).toBe('AQIDBAUG');
  });

  it('exige nova revisão após voltar para editar os valores', async () => {
    const api = criarApi();
    const fixture = await montarComponente(api);
    const root = fixture.nativeElement as HTMLElement;

    abrirContagem(fixture);
    preencherContagem(fixture, '6', '0', '0');
    clicar(fixture, 'Revisar contagem');
    clicar(fixture, 'Voltar e editar');

    expect(root.querySelector('#count-summary-title')).toBeNull();
    expect(root.querySelector<HTMLButtonElement>('button[type="submit"]')?.textContent).toContain(
      'Revisar contagem',
    );
    expect(root.querySelector<HTMLInputElement>('#count-vendavel')?.readOnly).toBe(false);
    preencher(fixture, '#count-vendavel', '7');
    clicar(fixture, 'Revisar contagem');
    expect(root.querySelector('#count-summary-title')?.parentElement?.textContent).toContain(
      '7 un.',
    );
    expect(api.registrarContagem).not.toHaveBeenCalled();
  });

  it('mostra validações próximas aos campos e foca o primeiro campo inválido', async () => {
    const api = criarApi();
    const fixture = await montarComponente(api);
    const root = fixture.nativeElement as HTMLElement;

    abrirContagem(fixture);
    clicar(fixture, 'Revisar contagem');

    expect(root.querySelector('#count-vendavel-error')?.textContent).toContain(
      'Digite 0 se não houver unidades.',
    );
    expect(root.querySelector('#count-reason-error')?.textContent).toContain(
      'Informe o motivo da contagem.',
    );
    expect(root.querySelector('#count-vendavel')?.getAttribute('aria-invalid')).toBe('true');
    expect(root.querySelector('#count-vendavel')?.getAttribute('aria-describedby')).toContain(
      'count-vendavel-error',
    );
    expect(document.activeElement).toBe(root.querySelector('#count-vendavel'));
    expect(api.registrarContagem).not.toHaveBeenCalled();
  });

  it('permite cancelar a contagem sem enviar operação', async () => {
    const api = criarApi();
    const fixture = await montarComponente(api);
    const root = fixture.nativeElement as HTMLElement;

    abrirContagem(fixture);
    preencher(fixture, '#count-vendavel', '4');
    clicar(fixture, 'Cancelar');

    expect(root.querySelector('#count-form')).toBeNull();
    expect(api.registrarContagem).not.toHaveBeenCalled();
  });

  it('envia a contagem com o snapshot original somente após revisar e confirmar', async () => {
    const api = criarApi();
    const fixture = await montarComponente(api);
    const root = fixture.nativeElement as HTMLElement;

    clicarConferirEstoque(fixture);
    expect(api.registrarContagem).not.toHaveBeenCalled();
    clicar(fixture, 'Confirmar contagem');
    await estabilizar(fixture);

    expect(api.registrarContagem).toHaveBeenCalledWith(
      'variant-1',
      {
        loteId: 'lot-1',
        quantidadeVendavel: 4,
        quantidadeDanificada: 2,
        quantidadeQuarentena: 0,
        motivo: 'Contagem física',
        versaoSaldoEsperada: 'AQIDBAUG',
      },
      expect.any(String),
    );
    expect(root.querySelector('#count-form')).toBeNull();
    expect(root.textContent).toContain('Conferência do lote registrada.');
  });

  it.each([
    'A posição mudou desde a leitura da contagem; atualize o saldo e repita.',
    'A posição foi alterada concorrentemente; repita a contagem.',
    'A contagem conflitou com outra alteração de estoque.',
  ])('atualiza explicitamente a versão 409 preservando os valores: %s', async (detail) => {
    const api = criarApi();
    const loteAtualizado = {
      ...loteInicial,
      versaoSaldo: 'BwgJCgsM',
      quantidadeVendavel: 8,
      quantidadeDanificada: 0,
      saldoFisico: 8,
      saldoUtilizavel: 8,
      saldoDisponivel: 8,
    };
    api.registrarContagem.mockReturnValueOnce(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: { detail },
          }),
      ),
    );
    api.consultarLote.mockReturnValueOnce(of(loteAtualizado));
    api.consultarPosicao
      .mockReturnValueOnce(of(criarPosicao()))
      .mockReturnValue(of(criarPosicao(loteAtualizado)));

    const fixture = await montarComponente(api);
    const root = fixture.nativeElement as HTMLElement;
    clicarConferirEstoque(fixture);
    clicar(fixture, 'Confirmar contagem');
    await estabilizar(fixture);

    expect(root.textContent).toContain('Atualize os dados quando estiver pronta');
    expect(api.registrarContagem.mock.calls[0]?.[1]?.versaoSaldoEsperada).toBe('AQIDBAUG');
    expect(api.consultarLote).not.toHaveBeenCalled();
    expect(document.activeElement).toBe(root.querySelector('[role="alert"]'));

    clicar(fixture, 'Atualizar dados mantendo os valores digitados');
    await estabilizar(fixture);
    expect(root.querySelector<HTMLInputElement>('#count-vendavel')?.value).toBe('4');
    expect(root.querySelector<HTMLInputElement>('#count-damaged')?.value).toBe('2');
    expect(root.querySelector<HTMLInputElement>('#count-quarantine')?.value).toBe('0');
    expect(root.querySelector<HTMLInputElement>('#count-reason')?.value).toBe('Contagem física');
    expect(root.querySelector('#count-summary-title')).toBeNull();
    expect(root.textContent).toContain('Revisar contagem');
    expect(root.textContent).not.toContain('Confirmar contagem');
    expect(api.registrarContagem).toHaveBeenCalledTimes(1);

    clicar(fixture, 'Revisar contagem');
    clicar(fixture, 'Confirmar contagem');
    await estabilizar(fixture);

    expect(api.registrarContagem).toHaveBeenCalledTimes(2);
    expect(api.registrarContagem.mock.calls[1]?.[1]).toMatchObject({
      quantidadeVendavel: 4,
      quantidadeDanificada: 2,
      quantidadeQuarentena: 0,
      motivo: 'Contagem física',
      versaoSaldoEsperada: 'BwgJCgsM',
    });
  });

  it('does not offer a data refresh for an unrelated key conflict', async () => {
    const api = criarApi();
    api.registrarContagem.mockReturnValueOnce(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: { detail: 'A chave de idempotência já foi usada com outros dados.' },
          }),
      ),
    );
    const fixture = await montarComponente(api);
    const root = fixture.nativeElement as HTMLElement;
    clicarConferirEstoque(fixture);
    clicar(fixture, 'Confirmar contagem');
    await estabilizar(fixture);

    expect(root.textContent).toContain('associada a dados diferentes');
    expect(root.textContent).not.toContain('Atualizar dados mantendo os valores digitados');
  });

  it('reutiliza a chave após falha de rede e recriação do componente, limpando após sucesso', async () => {
    const api = criarApi();
    api.registrarEntrada
      .mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 0 })))
      .mockReturnValueOnce(of({ id: 'movement-replayed' }));

    let fixture = await montarComponente(api);
    let root = fixture.nativeElement as HTMLElement;
    preencherEntrada(fixture, '4');
    clicar(fixture, 'Confirmar entrada');
    await estabilizar(fixture);

    const originalKey = api.registrarEntrada.mock.calls[0]?.[2];
    expect(originalKey).toBeTruthy();
    expect(sessionStorage.length).toBe(1);
    const stored = sessionStorage.getItem(sessionStorage.key(0)!);
    expect(stored).toContain(originalKey);
    expect(stored).not.toContain('Recebimento reservado');
    expect(root.textContent).toContain('Não foi possível confirmar o resultado');
    expect(root.textContent).not.toContain('idempotência');
    expect(root.textContent).not.toContain('payload');

    fixture.destroy();
    TestBed.resetTestingModule();
    fixture = await montarComponente(api);
    root = fixture.nativeElement as HTMLElement;
    preencherEntrada(fixture, '4');
    clicar(fixture, 'Confirmar entrada');
    await estabilizar(fixture);

    expect(api.registrarEntrada.mock.calls[1]?.[2]).toBe(originalKey);
    expect(sessionStorage.length).toBe(0);
    expect(root.textContent).toContain('Movimentação registrada com sucesso');
  });

  it('usa nova chave para payload alterado sem descartar a tentativa de resultado incerto', async () => {
    const api = criarApi();
    const response = new Subject<{ id: string }>();
    api.registrarEntrada
      .mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 0 })))
      .mockReturnValueOnce(response);

    const fixture = await montarComponente(api);
    const root = fixture.nativeElement as HTMLElement;
    preencherEntrada(fixture, '4');
    clicar(fixture, 'Confirmar entrada');
    await estabilizar(fixture);
    const firstKey = api.registrarEntrada.mock.calls[0]?.[2];

    preencher(fixture, '#finished-entry-quantity', '5');
    clicar(fixture, 'Confirmar entrada');
    await vi.waitFor(() => expect(api.registrarEntrada).toHaveBeenCalledTimes(2));
    fixture.detectChanges();

    const secondKey = api.registrarEntrada.mock.calls[1]?.[2];
    expect(secondKey).toBeTruthy();
    expect(secondKey).not.toBe(firstKey);
    expect(sessionStorage.length).toBe(2);

    response.next({ id: 'movement-5' });
    response.complete();
    await estabilizar(fixture);
    expect(sessionStorage.length).toBe(1);
    expect(sessionStorage.getItem(sessionStorage.key(0)!)).toContain(firstKey);
  });

  it('isola chaves pendentes por conta ADMIN', async () => {
    const api = criarApi();
    api.registrarEntrada
      .mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 0 })))
      .mockReturnValueOnce(new Subject<{ id: string }>());

    let fixture = await montarComponente(api, 'actor-1');
    let root = fixture.nativeElement as HTMLElement;
    preencherEntrada(fixture, '4');
    clicar(fixture, 'Confirmar entrada');
    await estabilizar(fixture);
    const firstKey = api.registrarEntrada.mock.calls[0]?.[2];
    fixture.destroy();
    TestBed.resetTestingModule();

    fixture = await montarComponente(api, 'actor-2');
    root = fixture.nativeElement as HTMLElement;
    preencherEntrada(fixture, '4');
    clicar(fixture, 'Confirmar entrada');
    await vi.waitFor(() => expect(api.registrarEntrada).toHaveBeenCalledTimes(2));
    fixture.detectChanges();

    expect(api.registrarEntrada.mock.calls[1]?.[2]).not.toBe(firstKey);
    expect(sessionStorage.length).toBe(2);
  });

  it('ignora cliques repetidos enquanto o envio está em andamento', async () => {
    const api = criarApi();
    const response = new Subject<{ id: string }>();
    api.registrarEntrada.mockReturnValue(response);
    const fixture = await montarComponente(api);
    const root = fixture.nativeElement as HTMLElement;

    preencherEntrada(fixture, '4');
    const button = Array.from(root.querySelectorAll<HTMLButtonElement>('button')).find((item) =>
      item.textContent?.includes('Confirmar entrada'),
    );
    button!.click();
    fixture.detectChanges();
    button!.click();
    await vi.waitFor(() => expect(api.registrarEntrada).toHaveBeenCalledTimes(1));
    response.next({ id: 'movement-1' });
    response.complete();
    await estabilizar(fixture);
  });
});
