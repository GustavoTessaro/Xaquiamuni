import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { routes } from '../../app.routes';
import { adminAuthGuard } from '../admin-auth.guard';
import { AdminAuthService } from '../../core/admin-auth/admin-auth.service';
import { ProdutosService } from '../../core/admin-produtos/produtos.service';
import { ProdutosAcabadosService } from '../../core/admin-estoque/produtos-acabados.service';
import {
  DetalheVenda,
  TentativaVenda,
  VendasExternasService,
} from '../../core/vendas-externas.service';
import { AdminVendasExternasComponent } from './admin-vendas-externas.component';

const id = '11111111-1111-4111-8111-111111111111';
const versao = '22222222-2222-4222-8222-222222222222';
const chave = '33333333-3333-4333-8333-333333333333';
const storageKey = 'xaquiamuni:vendas-externas:v1:admin';
const venda: DetalheVenda = {
  resumo: {
    id,
    versao,
    canal: 'Feira',
    dataVenda: '2026-01-05',
    situacao: 'Registrada',
    situacaoPagamento: 'Pendente',
    totalCentavos: '2400',
    recebidoCentavos: '0',
    saldoCentavos: '2400',
    comprador: 'Cliente sintético',
  },
  comprador: 'Cliente sintético',
  descricaoCanal: null,
  vencimento: null,
  canceladaEmUtc: null,
  motivoCancelamento: null,
  itens: [],
  recebimentos: [],
  baixas: [],
};

describe('AdminVendasExternasComponent', () => {
  let fixture: ComponentFixture<AdminVendasExternasComponent>;
  const api = {
    listar: vi.fn(),
    detalhe: vi.fn(),
    tentativa: vi.fn(),
    enviar: vi.fn(),
  };
  let idDireto: string | undefined;
  beforeEach(() => {
    idDireto = undefined;
    sessionStorage.removeItem(storageKey);
    vi.resetAllMocks();
    api.listar.mockReturnValue(of([venda.resumo]));
    api.detalhe.mockReturnValue(of(venda));
    api.enviar.mockReturnValue(of(venda.resumo));
    api.tentativa.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 })));
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useFactory: () => {
            const params = convertToParamMap(idDireto ? { id: idDireto } : {});
            return { paramMap: of(params), snapshot: { paramMap: params } };
          },
        },
        {
          provide: AdminAuthService,
          useValue: { getCurrentSession: () => of({ userId: 'admin' }) },
        },
        { provide: VendasExternasService, useValue: api },
        {
          provide: ProdutosService,
          useValue: {
            listar: () =>
              of([
                {
                  id: 'p1',
                  nome: 'Sabonete',
                  variantes: [{ id: 'v1', ativa: true, precoVenda: 10, identificacao: 'Lavanda' }],
                },
                {
                  id: 'p2',
                  nome: 'Vela',
                  variantes: [{ id: 'v2', ativa: true, precoVenda: 5, identificacao: 'Ervas' }],
                },
              ]),
          },
        },
        {
          provide: ProdutosAcabadosService,
          useValue: { consultarPosicao: () => of({ saldoDisponivel: 3 }) },
        },
      ],
    });
  });
  afterEach(() => sessionStorage.removeItem(storageKey));

  async function render() {
    fixture = TestBed.createComponent(AdminVendasExternasComponent);
    await update();
  }
  async function update() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }
  function button(texto: string) {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find(
      (el) => el.textContent?.trim() === texto,
    );
  }
  async function click(texto: string) {
    const el = button(texto);
    expect(el).toBeDefined();
    el!.click();
    await update();
  }
  async function field(nome: string, valor: string) {
    const el = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>(
      `[name="${nome}"]`,
    )!;
    expect(el).not.toBeNull();
    el.value = valor;
    el.dispatchEvent(new Event(el.tagName === 'SELECT' ? 'change' : 'input'));
    await update();
  }
  async function add(id: string) {
    await field('produto', id);
    await click('Adicionar item');
  }
  function checkbox() {
    return (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>(
      'input[name="confirmado"]',
    )!;
  }

  it('protege a rota de detalhe com o guarda administrativo', () => {
    expect(routes.find((rota) => rota.path === 'admin/vendas-externas/:id')?.canActivate).toContain(
      adminAuthGuard,
    );
  });

  it.each([true, false])('carrega a venda por URL direta (existe: %s)', async (existe) => {
    idDireto = id;
    if (!existe)
      api.detalhe.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 })));
    await render();
    expect(api.detalhe).toHaveBeenCalledExactlyOnceWith(id);
    expect(api.listar).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain(
      existe ? 'Venda de 05/01/2026' : 'Venda não encontrada.',
    );
  });

  it('lista comprador, filtra por canal e pagamento e abre o detalhe', async () => {
    await render();
    expect(fixture.nativeElement.textContent).toContain('Cliente sintético');
    await field('filtroCanal', 'Feira');
    await field('filtroSituacao', 'Pendente');
    expect(api.listar).toHaveBeenLastCalledWith('Feira', 'Pendente', 1);
    await click('Abrir venda');
    expect(api.detalhe).toHaveBeenCalledWith(id);
    expect(button('Cancelar venda')).toBeDefined();
    expect(button('Registrar recebimento')).toBeDefined();
  });

  it.each(['pendente', 'parcial', 'integral'] as const)(
    'registra dois itens e pagamento %s em centavos',
    async (modo) => {
      await render();
      await click('Nova venda');
      await add('v1');
      await add('v2');
      await field('quantidade-0', '2');
      await field('desconto-0', '1,00');
      await field('pagamento', modo);
      if (modo === 'parcial') await field('valor', '10,01');
      if (modo !== 'integral') {
        await click('Registrar venda');
        expect(api.enviar).not.toHaveBeenCalled();
        await field('comprador', 'Comprador de teste');
      }
      await click('Registrar venda');
      expect(api.enviar).toHaveBeenCalledWith(
        expect.objectContaining({
          tipo: 'criar',
          chave: expect.stringMatching(/^[a-f0-9-]{36}$/),
          corpo: expect.objectContaining({
            itens: [
              {
                produtoId: 'p1',
                varianteId: 'v1',
                quantidade: 2,
                precoUnitarioCentavos: '1000',
                descontoCentavos: '100',
              },
              {
                produtoId: 'p2',
                varianteId: 'v2',
                quantidade: 1,
                precoUnitarioCentavos: '500',
                descontoCentavos: '0',
              },
            ],
            recebimentoInicial:
              modo === 'pendente'
                ? null
                : expect.objectContaining({
                    valorCentavos: modo === 'parcial' ? '1001' : '2400',
                  }),
          }),
        }),
      );
      expect(sessionStorage.getItem(storageKey)).toBeNull();
    },
  );

  it('mantém a tentativa após 503 e só reenvia manualmente com os mesmos dados', async () => {
    api.enviar.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 503 })));
    await render();
    await click('Nova venda');
    await add('v1');
    await field('comprador', 'Comprador de teste');
    await click('Registrar venda');
    const tentativa = api.enviar.mock.calls[0][0];
    fixture.destroy();
    await render();
    expect(api.tentativa).toHaveBeenCalledWith(tentativa.chave, 'criar');
    expect(api.enviar).toHaveBeenCalledTimes(1);
    await click('Reenviar tentativa');
    expect(api.enviar).toHaveBeenNthCalledWith(2, tentativa);
  });

  it('abre a venda já gravada após recarga, sem outro POST', async () => {
    sessionStorage.setItem(storageKey, JSON.stringify({ tipo: 'criar', chave, corpo: {} }));
    api.tentativa.mockReturnValue(of(venda.resumo));
    await render();
    expect(api.detalhe).toHaveBeenCalledWith(id);
    expect(api.enviar).not.toHaveBeenCalled();
    expect(sessionStorage.getItem(storageKey)).toBeNull();
  });

  it('confirma recebimento uma vez e oculta cancelamento após atualizar o histórico', async () => {
    const retorno = new Subject<typeof venda.resumo>();
    api.enviar.mockReturnValue(retorno);
    await render();
    await click('Abrir venda');
    await click('Registrar recebimento');
    await field('valorPosterior', '5,50');
    expect(button('Confirmar recebimento')?.disabled).toBe(true);
    checkbox().click();
    await update();
    const confirmar = button('Confirmar recebimento')!;
    confirmar.click();
    confirmar.click();
    await update();
    expect(button('Confirmar recebimento')).toBeUndefined();
    expect(api.enviar).toHaveBeenCalledTimes(1);
    expect(api.enviar.mock.calls[0][0].corpo.valorCentavos).toBe('550');
    api.detalhe.mockReturnValue(
      of({
        ...venda,
        resumo: { ...venda.resumo, saldoCentavos: '1850' },
        recebimentos: [{ id, data: venda.resumo.dataVenda, meio: 'Pix', valorCentavos: '550' }],
      }),
    );
    retorno.next(venda.resumo);
    retorno.complete();
    await update();
    expect(button('Cancelar venda')).toBeUndefined();
    expect(fixture.nativeElement.textContent).toContain('5,50');
  });

  it('bloqueia reenvio de cancelamento após alteração concorrente', async () => {
    const tentativa: TentativaVenda = {
      tipo: 'cancelar',
      id,
      versao,
      chave,
      corpo: { motivo: 'Motivo de teste' },
    };
    sessionStorage.setItem(storageKey, JSON.stringify(tentativa));
    api.detalhe.mockReturnValue(of({ ...venda, resumo: { ...venda.resumo, versao: chave } }));
    await render();
    expect(api.detalhe).toHaveBeenCalledWith(id);
    expect(button('Reenviar tentativa')).toBeUndefined();
    expect(fixture.nativeElement.textContent).toContain('O reenvio foi bloqueado');
    expect(api.enviar).not.toHaveBeenCalled();
  });

  it('mostra o conflito de estoque e preserva os itens até conferir o resultado', async () => {
    api.enviar.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: { detail: 'Saldo disponível insuficiente.' },
          }),
      ),
    );
    await render();
    await click('Nova venda');
    await add('v1');
    await field('comprador', 'Comprador de teste');
    await click('Registrar venda');
    expect(fixture.nativeElement.textContent).toContain('Saldo disponível insuficiente.');
    expect(JSON.parse(sessionStorage.getItem(storageKey)!).corpo.itens).toHaveLength(1);
    expect(button('Reenviar tentativa')).toBeUndefined();
  });

  it('exige motivo e confirmação para cancelar e exibe a venda cancelada', async () => {
    const cancelada = {
      ...venda,
      resumo: {
        ...venda.resumo,
        situacao: 'Cancelada',
        situacaoPagamento: 'Cancelada',
        saldoCentavos: '0',
      },
      motivoCancelamento: 'Venda registrada por engano',
    };
    api.detalhe.mockReturnValueOnce(of(venda)).mockReturnValue(of(cancelada));
    await render();
    await click('Abrir venda');
    await click('Cancelar venda');
    checkbox().click();
    await update();
    expect(button('Confirmar cancelamento')?.disabled).toBe(true);
    await field('motivo', cancelada.motivoCancelamento);
    await click('Confirmar cancelamento');
    expect(api.enviar).toHaveBeenCalledWith(
      expect.objectContaining({
        tipo: 'cancelar',
        corpo: { motivo: cancelada.motivoCancelamento },
      }),
    );
    expect(fixture.nativeElement.textContent).toContain('Cancelada');
    expect(button('Cancelar venda')).toBeUndefined();
    expect(button('Registrar recebimento')).toBeUndefined();
  });
});
