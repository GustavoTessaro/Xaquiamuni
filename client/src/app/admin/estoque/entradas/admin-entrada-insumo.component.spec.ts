import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { AdminAuthService } from '../../../core/admin-auth/admin-auth.service';
import {
  AdminInsumoDetalhePreciso,
  AdminMovimentoInsumo,
  InsumosService,
  RegistrarEntradaInsumoRequest,
} from '../../../core/admin-estoque/insumos.service';
import { AdminEntradaInsumoComponent } from './admin-entrada-insumo.component';

const resumoBase: AdminInsumoDetalhePreciso['resumo'] = {
  insumoId: 'insumo-1',
  nome: 'Base glicerinada',
  tipo: 'MateriaPrima',
  unidadeBase: 'Grama',
  ativo: true,
  controlaLote: false,
  validadeObrigatoria: false,
  saldoFisico: '0',
  saldoUtilizavel: '0',
  saldoReservado: '0',
  saldoDisponivel: '0',
  estoqueMinimo: null,
  estoqueMinimoConfigurado: false,
  situacao: 'MinimoNaoConfigurado',
};

const detalheBase: AdminInsumoDetalhePreciso = {
  resumo: resumoBase,
  formasCompra: [
    {
      id: 'forma-1',
      nome: 'Pacote de 500 g',
      quantidadeEquivalentePorEmbalagem: '500',
      quantidadeEquivalentePorEmbalagemDecimal: '500',
      unidade: 'Grama',
      ativa: true,
    },
  ],
  posicoes: [],
};

const movimento: AdminMovimentoInsumo = {
  id: 'movement-1',
  tipo: 'Aquisicao',
  direcao: 'Entrada',
  quantidadeOriginal: 2,
  unidadeOriginal: 'Quilograma',
  quantidadeBase: 2000,
  unidadeBase: 'Grama',
  saldoAnterior: 0,
  saldoPosterior: 2000,
  motivo: 'Aquisicao',
  observacao: null,
  responsavelId: 'actor-1',
  registradoEmUtc: '2026-11-16T12:00:00Z',
  origem: 'FormaCompra',
  loteId: null,
  identificacaoLote: null,
  formaCompraId: null,
  formaCompraNome: null,
  fatorCompra: null,
  unidadeFatorCompra: null,
  custoTotal: null,
  custoPorUnidadeBase: null,
  movimentoOriginalId: null,
};

describe('AdminEntradaInsumoComponent', () => {
  beforeEach(() => {
    sessionStorage.clear();
  });

  afterEach(() => {
    sessionStorage.clear();
    TestBed.resetTestingModule();
  });

  it('converts a direct kg quantity to base grams and confirms before posting', async () => {
    const api = criarApi(detalheBase);
    const harness = await renderEntry(api);
    const root = harness.routeNativeElement;
    expect(root).not.toBeNull();
    expect(root!.querySelector('a[href="/admin/estoque/insumos/insumo-1"]')).not.toBeNull();

    setSelect(harness, '#stock-entry-unit', 'Quilograma');
    setInput(harness, '#stock-entry-quantity', '2');
    expect(root!.textContent).toContain('Equivalência: 2.000 g');

    click(harness, 'button[type="submit"]');
    expect(root!.textContent).toContain('Revise a entrada antes de registrar');
    expect(root!.textContent).toContain('2.000 g');
    expect(api.registrarEntrada).not.toHaveBeenCalled();

    clickText(harness, 'Confirmar entrada');
    const request = api.registrarEntrada.mock.calls[0]?.[1] as RegistrarEntradaInsumoRequest;
    expect(request).toMatchObject({
      tipo: 'Aquisicao',
      quantidadeOriginal: '2',
      unidadeOriginal: 'Quilograma',
      formaCompraId: null,
      custoTotal: null,
    });
    expect(request).not.toHaveProperty('responsavelId');
    expect(root!.textContent).toContain('Entrada registrada');
    expect(root!.textContent).toContain('movement-1');
  });

  it('uses the purchase-form factor exactly and preserves known zero cost', async () => {
    const detail: AdminInsumoDetalhePreciso = {
      ...detalheBase,
      formasCompra: [
        {
          ...detalheBase.formasCompra[0]!,
          quantidadeEquivalentePorEmbalagem: '500.25',
          quantidadeEquivalentePorEmbalagemDecimal: '500.25',
        },
      ],
    };
    const api = criarApi(detail);
    const harness = await renderEntry(api);
    const root = harness.routeNativeElement!;

    setSelect(harness, '#stock-entry-quantity-mode', 'FormaCompra');
    setSelect(harness, '#stock-entry-purchase-form', 'forma-1');
    setInput(harness, '#stock-entry-quantity', '3');
    setInput(harness, '#stock-entry-cost', '0');
    expect(root.textContent).toContain('Equivalência: 1.500,75 g');

    click(harness, 'button[type="submit"]');
    expect(root.textContent).toContain('R$ 0,00');
    clickText(harness, 'Confirmar entrada');
    const request = api.registrarEntrada.mock.calls[0]?.[1] as RegistrarEntradaInsumoRequest;
    expect(request).toMatchObject({
      quantidadeOriginal: '3',
      unidadeOriginal: null,
      formaCompraId: 'forma-1',
      custoTotal: '0',
    });
  });

  it('converts liters to milliliters', async () => {
    const detail: AdminInsumoDetalhePreciso = {
      ...detalheBase,
      resumo: { ...resumoBase, unidadeBase: 'Mililitro' },
      formasCompra: [],
    };
    const api = criarApi(detail);
    const harness = await renderEntry(api);
    const root = harness.routeNativeElement!;

    setSelect(harness, '#stock-entry-unit', 'Litro');
    setInput(harness, '#stock-entry-quantity', '1,5');
    expect(root.textContent).toContain('Equivalência: 1.500 ml');
    click(harness, 'button[type="submit"]');
    clickText(harness, 'Confirmar entrada');
    expect(api.registrarEntrada.mock.calls[0]?.[1]).toMatchObject({
      quantidadeOriginal: '1.5',
      unidadeOriginal: 'Litro',
    });
  });

  it('rejects fractional package counts', async () => {
    const api = criarApi(detalheBase);
    const harness = await renderEntry(api);
    setSelect(harness, '#stock-entry-quantity-mode', 'FormaCompra');
    setSelect(harness, '#stock-entry-purchase-form', 'forma-1');
    setInput(harness, '#stock-entry-quantity', '1,5');
    click(harness, 'button[type="submit"]');
    expect(harness.routeNativeElement?.textContent).toContain(
      'A quantidade de embalagens deve ser um número inteiro positivo.',
    );
    expect(api.registrarEntrada).not.toHaveBeenCalled();
  });

  it('rejects fractional quantities for indivisible base units', async () => {
    const detail: AdminInsumoDetalhePreciso = {
      ...detalheBase,
      resumo: { ...resumoBase, unidadeBase: 'Unidade' },
      formasCompra: [],
    };
    const api = criarApi(detail);
    const harness = await renderEntry(api);
    setInput(harness, '#stock-entry-quantity', '2,5');
    click(harness, 'button[type="submit"]');
    expect(harness.routeNativeElement?.textContent).toContain(
      'unidade inteira permitida pelo estoque',
    );
    expect(api.registrarEntrada).not.toHaveBeenCalled();
  });

  it('registers stock without dates or lot selection for a historical supply', async () => {
    const api = criarApi({
      ...detalheBase,
      resumo: { ...resumoBase, controlaLote: true, validadeObrigatoria: true },
    });
    const harness = await renderEntry(api);
    const root = harness.routeNativeElement!;
    expect(root.querySelector('input[type=date], #stock-entry-lot')).toBeNull();
    setInput(harness, '#stock-entry-quantity', '5');
    click(harness, 'button[type="submit"]');
    clickText(harness, 'Confirmar entrada');
    expect(api.registrarEntrada.mock.calls[0]?.[1]).toMatchObject({
      loteId: null,
      dataValidadeNovoLote: null,
      semControleValidade: true,
    });
  });

  it('retries an uncertain write with the original idempotency key and unchanged payload', async () => {
    const api = criarApi(detalheBase);
    api.registrarEntrada
      .mockReturnValueOnce(
        throwError(
          () => new HttpErrorResponse({ status: 0, error: new ProgressEvent('network-error') }),
        ),
      )
      .mockReturnValueOnce(of(movimento));
    const harness = await renderEntry(api);

    setInput(harness, '#stock-entry-quantity', '2');
    click(harness, 'button[type="submit"]');
    clickText(harness, 'Confirmar entrada');
    const root = harness.routeNativeElement!;
    expect(root.textContent).toContain('Confirmação pendente');
    const firstKey = api.registrarEntrada.mock.calls[0]?.[2];
    const firstRequest = api.registrarEntrada.mock.calls[0]?.[1];

    clickText(harness, 'Tentar novamente com os mesmos dados');
    const secondCall = api.registrarEntrada.mock.calls[1];
    expect(secondCall?.[2]).toBe(firstKey);
    expect(secondCall?.[1]).toEqual(firstRequest);
    expect(root.textContent).toContain('Entrada registrada');
    expect(root.textContent).toContain('Código para consulta no histórico');
    expect(root.querySelector('details code')?.textContent).toBe('movement-1');
    expect(api.obterParaEntrada).toHaveBeenCalledTimes(2);
  });

  it('explains a conflicting attempt without offering an unsafe retry', async () => {
    const api = criarApi(detalheBase);
    api.registrarEntrada.mockReturnValueOnce(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: { detail: 'A chave de idempotência já está associada a outro conteúdo.' },
          }),
      ),
    );
    const harness = await renderEntry(api);

    setInput(harness, '#stock-entry-quantity', '2');
    click(harness, 'button[type="submit"]');
    clickText(harness, 'Confirmar entrada');
    expect(harness.routeNativeElement?.textContent).toContain('Verifique a tentativa anterior');
    expect(harness.routeNativeElement?.textContent).toContain(
      'confira o histórico antes de iniciar outra entrada',
    );
    expect(harness.routeNativeElement?.textContent).not.toContain('idempotência');
    expect(
      Array.from(harness.routeNativeElement?.querySelectorAll('button') ?? []).some((button) =>
        button.textContent?.includes('Tentar novamente'),
      ),
    ).toBe(false);
    expect(sessionStorage.getItem('xaquiamuni:estoque:entrada:pendente')).not.toBeNull();
  });
});

function criarApi(detalhe: AdminInsumoDetalhePreciso) {
  return {
    listar: vi.fn(() =>
      of({ itens: [detalhe.resumo], totalItens: 1, pagina: 1, itensPorPagina: 20 }),
    ),
    obterParaEntrada: vi.fn(() => of(detalhe)),
    registrarEntrada: vi.fn(
      (_id: string, _request: RegistrarEntradaInsumoRequest, _idempotencyKey: string) =>
        of(movimento),
    ),
  };
}

async function renderEntry(api: ReturnType<typeof criarApi>): Promise<RouterTestingHarness> {
  await TestBed.configureTestingModule({
    imports: [AdminEntradaInsumoComponent],
    providers: [
      provideRouter([
        { path: 'admin/estoque/insumos/entradas', component: AdminEntradaInsumoComponent },
      ]),
      {
        provide: AdminAuthService,
        useValue: {
          getCurrentSession: vi.fn(() => of({ userId: 'actor-1' })),
          logout: vi.fn(() => of(undefined)),
        },
      },
      { provide: InsumosService, useValue: api },
    ],
  }).compileComponents();
  const harness = await RouterTestingHarness.create();
  await harness.navigateByUrl(
    '/admin/estoque/insumos/entradas?insumoId=insumo-1',
    AdminEntradaInsumoComponent,
  );
  await harness.fixture.whenStable();
  harness.fixture.detectChanges();
  return harness;
}

function setInput(harness: RouterTestingHarness, selector: string, value: string): void {
  const root = harness.routeNativeElement!;
  const input = root.querySelector<HTMLInputElement>(selector);
  if (!input) {
    throw new Error(`Input not found: ${selector}`);
  }
  input.value = value;
  input.dispatchEvent(new Event('input', { bubbles: true }));
  input.dispatchEvent(new Event('change', { bubbles: true }));
  harness.fixture.detectChanges();
}

function setSelect(harness: RouterTestingHarness, selector: string, value: string): void {
  const root = harness.routeNativeElement!;
  const select = root.querySelector<HTMLSelectElement>(selector);
  if (!select) {
    throw new Error(`Select not found: ${selector}`);
  }
  select.value = value;
  select.dispatchEvent(new Event('change', { bubbles: true }));
  harness.fixture.detectChanges();
}

function setRadio(harness: RouterTestingHarness, value: string): void {
  const root = harness.routeNativeElement!;
  const radio = root.querySelector<HTMLInputElement>(`input[type="radio"][value="${value}"]`);
  if (!radio) {
    throw new Error(`Radio option not found: ${value}`);
  }
  radio.checked = true;
  radio.dispatchEvent(new Event('change', { bubbles: true }));
  harness.fixture.detectChanges();
}

function click(harness: RouterTestingHarness, selector: string): void {
  const root = harness.routeNativeElement!;
  const element = root.querySelector<HTMLElement>(selector);
  if (!element) {
    throw new Error(`Element not found: ${selector}`);
  }
  element.click();
  harness.fixture.detectChanges();
}

function clickText(harness: RouterTestingHarness, text: string): void {
  const root = harness.routeNativeElement!;
  const element = Array.from(root.querySelectorAll('button')).find((button) =>
    button.textContent?.includes(text),
  );
  if (!element) {
    throw new Error(`Button not found: ${text}`);
  }
  element.click();
  harness.fixture.detectChanges();
}
