import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { AdminAuthService } from '../../../core/admin-auth/admin-auth.service';
import {
  AdminFormaCompra,
  AdminInsumoDetalhe,
  AdminInsumoResumo,
  CadastrarInsumoRequest,
  InsumosService,
} from '../../../core/admin-estoque/insumos.service';
import { AdminInsumoFormComponent } from './admin-insumo-form.component';

describe('AdminInsumoFormComponent', () => {
  let fixture: ComponentFixture<AdminInsumoFormComponent>;
  let routeId: string | null;
  let obter: ReturnType<typeof vi.fn<InsumosService['obter']>>;
  let criar: ReturnType<typeof vi.fn<InsumosService['criar']>>;
  let editar: ReturnType<typeof vi.fn<InsumosService['editar']>>;
  let alterarStatus: ReturnType<typeof vi.fn<InsumosService['alterarStatus']>>;
  let criarFormaCompra: ReturnType<typeof vi.fn<InsumosService['criarFormaCompra']>>;
  let editarFormaCompra: ReturnType<typeof vi.fn<InsumosService['editarFormaCompra']>>;
  let alterarStatusFormaCompra: ReturnType<
    typeof vi.fn<InsumosService['alterarStatusFormaCompra']>
  >;
  let navigate: ReturnType<typeof vi.fn<Router['navigate']>>;

  const resumo: AdminInsumoResumo = {
    insumoId: 'insumo-1',
    nome: 'Base glicerinada',
    tipo: 'MateriaPrima',
    unidadeBase: 'Grama',
    ativo: true,
    controlaLote: false,
    validadeObrigatoria: false,
    saldoFisico: 0,
    saldoUtilizavel: 0,
    saldoReservado: 0,
    saldoDisponivel: 0,
    estoqueMinimo: null,
    estoqueMinimoConfigurado: false,
    situacao: 'MinimoNaoConfigurado',
  };
  const detalhe: AdminInsumoDetalhe = {
    resumo,
    formasCompra: [],
    posicoes: [],
  };

  beforeEach(async () => {
    routeId = null;
    obter = vi.fn<InsumosService['obter']>().mockReturnValue(of(detalhe));
    criar = vi.fn<InsumosService['criar']>().mockReturnValue(of(detalhe));
    editar = vi.fn<InsumosService['editar']>().mockReturnValue(of(detalhe));
    alterarStatus = vi.fn<InsumosService['alterarStatus']>().mockReturnValue(of(detalhe));
    criarFormaCompra = vi.fn<InsumosService['criarFormaCompra']>();
    editarFormaCompra = vi.fn<InsumosService['editarFormaCompra']>();
    alterarStatusFormaCompra = vi.fn<InsumosService['alterarStatusFormaCompra']>();

    await TestBed.configureTestingModule({
      imports: [AdminInsumoFormComponent],
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useFactory: () => ({
            snapshot: {
              paramMap: { get: (name: string) => (name === 'id' ? routeId : null) },
            },
          }),
        },
        {
          provide: InsumosService,
          useValue: {
            obter,
            criar,
            editar,
            alterarStatus,
            criarFormaCompra,
            editarFormaCompra,
            alterarStatusFormaCompra,
          },
        },
        { provide: AdminAuthService, useValue: { logout: vi.fn(() => of(undefined)) } },
      ],
    }).compileComponents();
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it('groups existing routine, consultation, and exceptional stock actions', () => {
    routeId = 'insumo-1';
    render();

    const nav = root().querySelector('nav[aria-label="Ações do insumo"]');
    expect(nav).not.toBeNull();
    expect(nav?.querySelector('#stock-supply-routine-title')?.textContent).toContain(
      'Rotina de estoque',
    );
    expect(nav?.querySelector('#stock-supply-check-title')?.textContent).toContain(
      'Conferir e consultar',
    );
    expect(nav?.querySelector('#stock-supply-exception-title')?.textContent).toContain(
      'Perdas e descartes',
    );
    for (const label of [
      'Registrar entrada',
      'Registrar saída',
      'Fazer contagem',
      'Posição e histórico',
      'Registrar perda ou descarte',
    ]) {
      expect(nav?.textContent).toContain(label);
    }
    expect(nav?.textContent).toContain('Registre materiais recebidos ou usados.');
    expect(root().querySelector('#stock-supply-routine-title')?.closest('section')).toBeTruthy();
  });

  it('validates required names and creates cadastro without adding stock', () => {
    render();
    clickButton('Cadastrar material');
    expect(text()).toContain('Informe o nome do insumo.');
    expect(criar).not.toHaveBeenCalled();

    setField('stock-supply-name', 'Base glicerinada');
    setSelect('stock-supply-base-unit', 'Unidade');
    setField('stock-supply-minimum', '0.5');
    clickButton('Cadastrar material');
    expect(text()).toContain('Para unidade inteira, o estoque mínimo deve ser um número inteiro.');
    expect(criar).not.toHaveBeenCalled();

    setField('stock-supply-name', ' Base glicerinada ');
    setSelect('stock-supply-base-unit', 'Grama');
    setField('stock-supply-minimum', '0.125');
    clickButton('Cadastrar material');

    expect(criar).toHaveBeenCalledOnce();
    expect(criar).toHaveBeenCalledWith({
      nome: 'Base glicerinada',
      tipo: 'MateriaPrima',
      unidadeBase: 'Grama',
      estoqueMinimo: 0.125,
      controlaLote: false,
      validadeObrigatoria: false,
    } satisfies CadastrarInsumoRequest);
    expect(criar.mock.calls[0]?.[0]).not.toHaveProperty('saldoInicial');
    expect(criar.mock.calls[0]?.[0]).not.toHaveProperty('responsavelId');
    expect(navigate).toHaveBeenCalledWith(['/admin/estoque/insumos', 'insumo-1'], {
      state: { mensagem: 'Insumo cadastrado sem movimentar estoque.' },
    });
  });

  it('requires lot control for validity and leaves the base unit unchanged during edits', () => {
    routeId = 'insumo-1';
    const item = {
      ...detalhe,
      resumo: {
        ...resumo,
        controlaLote: true,
        validadeObrigatoria: true,
      },
    };
    obter.mockReturnValue(of(item));
    render();

    expect(obter).toHaveBeenCalledWith('insumo-1');
    expect(
      root().querySelector<HTMLAnchorElement>(
        'a[href="/admin/estoque/insumos/insumo-1/consultas"]',
      ),
    ).not.toBeNull();
    expect((root().querySelector('#stock-supply-base-unit') as HTMLSelectElement).disabled).toBe(
      true,
    );
    expect(
      (root().querySelector('input[formControlName="validadeObrigatoria"]') as HTMLInputElement)
        .disabled,
    ).toBe(false);

    setCheckbox('input[formControlName="controlaLote"]', false);
    expect(
      (root().querySelector('input[formControlName="validadeObrigatoria"]') as HTMLInputElement)
        .checked,
    ).toBe(false);
    expect(
      (root().querySelector('input[formControlName="validadeObrigatoria"]') as HTMLInputElement)
        .disabled,
    ).toBe(true);
  });

  it('explains protected policy conflicts and preserves the edited values', () => {
    routeId = 'insumo-1';
    editar.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: { detail: 'SQL or persistence details' },
          }),
      ),
    );
    render();

    setField('stock-supply-name', 'Base revisada');
    setCheckbox('input[formControlName="controlaLote"]', true);
    setCheckbox('input[formControlName="validadeObrigatoria"]', true);
    clickButton('Salvar alterações');

    expect(editar).toHaveBeenCalledWith('insumo-1', {
      nome: 'Base revisada',
      tipo: 'MateriaPrima',
      estoqueMinimo: null,
      controlaLote: true,
      validadeObrigatoria: true,
    });
    expect(text()).toContain('já possui lotes, saldos ou movimentações');
    expect(text()).not.toContain('SQL or persistence details');
    expect((root().querySelector('#stock-supply-name') as HTMLInputElement).value).toBe(
      'Base revisada',
    );
  });

  it('converts purchase packaging into the canonical base unit and keeps whole units integral', () => {
    routeId = 'insumo-1';
    render();
    clickButton('Nova forma de compra');
    setField('purchase-form-name', 'Pacote de 500 g');
    setSelect('purchase-form-unit', 'Quilograma');
    setField('purchase-form-quantity', '0.5');

    expect(text()).toContain('Equivale a 500 g por embalagem.');
    const forma: AdminFormaCompra = {
      id: 'forma-1',
      nome: 'Pacote de 500 g',
      quantidadeEquivalentePorEmbalagem: 500,
      unidade: 'Grama',
      ativa: true,
    };
    criarFormaCompra.mockReturnValue(of(forma));
    clickButton('Cadastrar forma');

    expect(criarFormaCompra).toHaveBeenCalledWith('insumo-1', {
      nome: 'Pacote de 500 g',
      quantidadeEquivalentePorEmbalagem: 0.5,
      unidade: 'Quilograma',
    });
    expect(text()).toContain('500 g na unidade-base por embalagem');

    clickButton('Nova forma de compra');
    setField('purchase-form-name', 'Dose de 1 micrograma');
    setSelect('purchase-form-unit', 'Quilograma');
    setField('purchase-form-quantity', '0.000000001');
    expect(text()).toContain('Equivale a 0,000001 g por embalagem.');
    criarFormaCompra.mockReturnValue(
      of({
        id: 'forma-micro',
        nome: 'Dose de 1 micrograma',
        quantidadeEquivalentePorEmbalagem: 0.000001,
        unidade: 'Grama',
        ativa: true,
      }),
    );
    clickButton('Cadastrar forma');
    expect(criarFormaCompra).toHaveBeenLastCalledWith('insumo-1', {
      nome: 'Dose de 1 micrograma',
      quantidadeEquivalentePorEmbalagem: 0.000000001,
      unidade: 'Quilograma',
    });

    const integerDetail: AdminInsumoDetalhe = {
      ...detalhe,
      resumo: { ...resumo, unidadeBase: 'Unidade' },
    };
    routeId = 'insumo-1';
    obter.mockReturnValue(of(integerDetail));
    fixture.destroy();
    render();
    clickButton('Nova forma de compra');
    setField('purchase-form-name', 'Pacote fracionário');
    setField('purchase-form-quantity', '0.5');
    clickButton('Cadastrar forma');

    expect(text()).toContain('A quantidade em unidades inteiras deve ser um número inteiro.');
    expect(criarFormaCompra).toHaveBeenCalledTimes(2);
  });

  it('edits and inactivates a purchase form through the dedicated API operations', () => {
    routeId = 'insumo-1';
    const form: AdminFormaCompra = {
      id: 'forma-1',
      nome: 'Pacote de 500 g',
      quantidadeEquivalentePorEmbalagem: 500,
      unidade: 'Grama',
      ativa: true,
    };
    obter.mockReturnValue(of({ ...detalhe, formasCompra: [form] }));
    editarFormaCompra.mockReturnValue(of({ ...form, nome: 'Pacote grande' }));
    alterarStatusFormaCompra.mockReturnValue(
      of({
        ...form,
        nome: 'Pacote grande',
        ativa: false,
      }),
    );
    vi.stubGlobal('confirm', vi.fn().mockReturnValue(true));
    render();

    clickButton('Editar');
    setField('purchase-form-name', 'Pacote grande');
    clickButton('Salvar forma');
    expect(editarFormaCompra).toHaveBeenCalledWith('insumo-1', 'forma-1', {
      nome: 'Pacote grande',
      quantidadeEquivalentePorEmbalagem: 500,
      unidade: 'Grama',
    });
    expect(text()).toContain('Forma de compra atualizada com sucesso.');

    clickButton('Inativar forma de compra Pacote grande');
    expect(alterarStatusFormaCompra).toHaveBeenCalledWith('insumo-1', 'forma-1', {
      ativa: false,
    });
    expect(text()).toContain('Inativa');
  });

  it('confirms inactivation and communicates the server conflict without leaking details', () => {
    routeId = 'insumo-1';
    alterarStatus.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: { message: 'persistence details' },
          }),
      ),
    );
    const confirm = vi.fn().mockReturnValue(true);
    vi.stubGlobal('confirm', confirm);
    render();
    clickButton('Inativar');

    expect(confirm).toHaveBeenCalledOnce();
    expect(alterarStatus).toHaveBeenCalledWith('insumo-1', { ativo: false });
    expect(text()).toContain('O status do insumo mudou em outra operação.');
    expect(text()).not.toContain('persistence details');
  });

  function render(): void {
    fixture = TestBed.createComponent(AdminInsumoFormComponent);
    fixture.detectChanges();
  }

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function text(): string {
    return root().textContent ?? '';
  }

  function setField(id: string, value: string): void {
    const input = root().querySelector(`#${id}`) as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
  }

  function setSelect(id: string, value: string): void {
    const select = root().querySelector(`#${id}`) as HTMLSelectElement;
    select.value = value;
    select.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
  }

  function setCheckbox(selector: string, checked: boolean): void {
    const checkbox = root().querySelector(selector) as HTMLInputElement;
    checkbox.checked = checked;
    checkbox.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
  }

  function clickButton(label: string): void {
    const button = Array.from(root().querySelectorAll('button')).find(
      (candidate) =>
        candidate.getAttribute('aria-label') === label || candidate.textContent?.trim() === label,
    );
    if (!button) {
      throw new Error(`Button not found: ${label}`);
    }
    button.click();
    fixture.detectChanges();
  }
});
