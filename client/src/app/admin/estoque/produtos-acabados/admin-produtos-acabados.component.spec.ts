import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { AdminAuthService } from '../../../core/admin-auth/admin-auth.service';
import { ProdutosService } from '../../../core/admin-produtos/produtos.service';
import {
  ContagemProdutoAcabadoRequest,
  EntradaProdutoAcabadoRequest,
  LoteProdutoAcabado,
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
    consultarPosicao: vi.fn(() => of(criarPosicao())),
    consultarLote: vi.fn(() => of(loteInicial)),
    consultarPolitica: vi.fn(() =>
      of({
        produtoVarianteId: 'variant-1',
        estoqueMinimo: 2,
        prazoValidadePadraoDias: 30,
      }),
    ),
    listarMovimentacoes: vi.fn(() =>
      of({ itens: [], totalItens: 0, pagina: 1, itensPorPagina: 20 }),
    ),
    registrarEntrada: vi.fn(
      (_varianteId: string, _request: EntradaProdutoAcabadoRequest, _key: string) =>
        of({ id: 'movement-1' }),
    ),
    registrarSaida: vi.fn(() => of({ id: 'movement-2' })),
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
): Promise<ComponentFixture<AdminProdutosAcabadosComponent>> {
  const params = convertToParamMap(varianteSelecionada ? { varianteId: 'variant-1' } : {});
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
      { provide: ProdutosService, useValue: { listar: () => of([produto]) } },
      { provide: ProdutosAcabadosService, useValue: api },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(AdminProdutosAcabadosComponent);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  return fixture;
}

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

function clicarConferirEstoque(fixture: ComponentFixture<AdminProdutosAcabadosComponent>): void {
  clicar(fixture, 'Conferir estoque');
  preencher(fixture, '#count-vendavel', '4');
  preencher(fixture, '#count-damaged', '2');
  preencher(fixture, '#count-quarantine', '0');
  preencher(fixture, '#count-reason', 'Contagem física');
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

  it('envia a contagem com o snapshot original', async () => {
    const api = criarApi();
    const fixture = await montarComponente(api);
    const root = fixture.nativeElement as HTMLElement;

    clicarConferirEstoque(fixture);
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

    expect(root.textContent).toContain('Atualize o snapshot quando estiver pronta');
    expect(api.registrarContagem.mock.calls[0]?.[1]?.versaoSaldoEsperada).toBe('AQIDBAUG');
    expect(api.consultarLote).not.toHaveBeenCalled();

    clicar(fixture, 'Atualizar snapshot mantendo os valores digitados');
    await estabilizar(fixture);
    expect(root.querySelector<HTMLInputElement>('#count-vendavel')?.value).toBe('4');
    expect(root.querySelector<HTMLInputElement>('#count-damaged')?.value).toBe('2');
    expect(root.querySelector<HTMLInputElement>('#count-reason')?.value).toBe('Contagem física');

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

  it('não oferece recarga de snapshot para conflito de idempotência', async () => {
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

    expect(root.textContent).toContain('associada a outro conteúdo');
    expect(root.textContent).not.toContain('Atualizar snapshot mantendo os valores digitados');
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
    expect(root.textContent).toContain('A chave foi preservada');

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
