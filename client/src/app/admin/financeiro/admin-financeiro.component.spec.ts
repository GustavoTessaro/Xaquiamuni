import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { AdminAuthService } from '../../core/admin-auth/admin-auth.service';
import { ComprasMateriasPrimasService } from '../../core/compras-materias-primas.service';
import {
  CompraFinanceira,
  FinanceiroService,
  LancamentoFinanceiro,
} from '../../core/financeiro.service';
import { AdminFinanceiroComponent, centavosFinanceiros } from './admin-financeiro.component';

const id = '11111111-1111-4111-8111-111111111111';
const chaveStorage = 'xaquiamuni:financeiro:v1:admin';
const lancamento: LancamentoFinanceiro = {
  id,
  tipo: 'Entrada',
  descricao: 'Aporte',
  categoria: 'Outros',
  valorCentavos: '9223372036854775807',
  data: '2026-11-20',
  meio: 'Pix',
  responsavelId: id,
  criadoEmUtc: '2026-11-20T12:00:00Z',
  canceladoPorId: null,
  canceladoEmUtc: null,
  motivoCancelamento: null,
};
const compra: CompraFinanceira = {
  compraId: id,
  fornecedorId: id,
  nomeFornecedor: 'Fornecedor teste',
  dataCompra: '2026-11-20',
  situacaoCompra: 'Recebida',
  situacaoFinanceira: 'Pendente',
  composicaoValor: 'Conhecido',
  totalCentavos: '10000',
  pagoCentavos: '0',
  saldoCentavos: '10000',
};

describe('AdminFinanceiroComponent', () => {
  let fixture: ComponentFixture<AdminFinanceiroComponent>;
  const api = {
    lancamentos: vi.fn(),
    lancamento: vi.fn(),
    compra: vi.fn(),
    pagamentos: vi.fn(),
    enviar: vi.fn(),
  };
  const rota = { snapshot: { queryParamMap: convertToParamMap({}) } };

  beforeEach(() => {
    sessionStorage.removeItem(chaveStorage);
    vi.resetAllMocks();
    rota.snapshot.queryParamMap = convertToParamMap({});
    api.lancamentos.mockReturnValue(of([lancamento]));
    api.lancamento.mockReturnValue(of(lancamento));
    api.compra.mockReturnValue(of(compra));
    api.pagamentos.mockReturnValue(of([]));
    api.enviar.mockReturnValue(of(lancamento));
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: rota },
        {
          provide: AdminAuthService,
          useValue: { getCurrentSession: () => of({ userId: 'admin' }) },
        },
        { provide: FinanceiroService, useValue: api },
        {
          provide: ComprasMateriasPrimasService,
          useValue: { listar: () => of([{ ...compra, id }]) },
        },
      ],
    });
  });
  afterEach(() => sessionStorage.removeItem(chaveStorage));

  async function render() {
    fixture = TestBed.createComponent(AdminFinanceiroComponent);
    await atualizar();
  }
  async function atualizar() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }
  function botao(rotulo: string) {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find(
      (el) => el.textContent?.trim() === rotulo,
    );
  }
  async function clicar(rotulo: string) {
    expect(botao(rotulo)).toBeDefined();
    botao(rotulo)!.click();
    await atualizar();
  }
  async function campo(nome: string, valor: string) {
    const el = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>(
      '[name="' + nome + '"]',
    )!;
    el.value = valor;
    el.dispatchEvent(new Event(el.tagName === 'SELECT' ? 'change' : 'input'));
    await atualizar();
  }
  function abrirDireto() {
    rota.snapshot.queryParamMap = convertToParamMap({ compra: id });
  }
  function texto() {
    return fixture.nativeElement.textContent as string;
  }
  async function preencher() {
    await clicar('Novo lançamento');
    await campo('categoria', 'Teste');
    await campo('descricao', 'Novo movimento');
  }

  it('mostra centavos grandes sem perda e carrega a próxima página', async () => {
    api.lancamentos.mockReturnValueOnce(
      of(Array.from({ length: 50 }, (_, i) => ({ ...lancamento, id: String(i) }))),
    );
    await render();
    expect(texto()).toContain('R$ 92.233.720.368.547.758,07');
    await clicar('Próxima');
    expect(api.lancamentos).toHaveBeenLastCalledWith(2);
    expect(texto()).toContain('Página 2');
  });

  it.each(['Entrada', 'Saida'])('cria %s usando centavos como string', async (tipo) => {
    await render();
    await preencher();
    await campo('tipo', tipo);
    await campo('valor', '1.234,56');
    await clicar('Confirmar');
    expect(api.enviar).toHaveBeenCalledWith(
      expect.objectContaining({
        tipo: 'criar',
        chave: expect.stringMatching(/^[a-f0-9-]{36}$/),
        corpo: expect.objectContaining({ tipo, valorCentavos: '123456' }),
      }),
    );
    expect(sessionStorage.getItem(chaveStorage)).toBeNull();
  });

  it('rejeita valor inválido', async () => {
    await render();
    await preencher();
    await campo('valor', '1,001');
    await clicar('Confirmar');
    expect(api.enviar).not.toHaveBeenCalled();
    expect(texto()).toContain('duas casas decimais');
  });

  it('exige motivo e confirmação para cancelar', async () => {
    api.enviar.mockReturnValue(
      of({ ...lancamento, canceladoEmUtc: lancamento.criadoEmUtc, motivoCancelamento: 'Correção' }),
    );
    await render();
    await clicar('Ver detalhe');
    await clicar('Cancelar lançamento');
    await campo('motivo', 'Correção');
    expect(botao('Confirmar')!.disabled).toBe(true);
    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLInputElement>('[name=confirmacao]')!
      .click();
    await atualizar();
    await clicar('Confirmar');
    expect(api.enviar).toHaveBeenCalledWith(
      expect.objectContaining({ tipo: 'cancelar', id, corpo: { motivo: 'Correção' } }),
    );
    expect(texto()).toContain('Cancelado');
  });

  it('seleciona uma compra existente e mostra a obrigação pendente', async () => {
    await render();
    await clicar('Pagamentos de compras');
    const seletor = (fixture.nativeElement as HTMLElement).querySelector<HTMLSelectElement>(
      'select',
    )!;
    seletor.value = id;
    await clicar('Consultar compra');
    expect(texto()).toContain('Pendente');
    expect(texto()).toContain('R$ 100,00');
  });

  it.each([
    ['Parcial', 'Parcialmente paga', '2500', '7500'],
    ['Quitada', 'Quitada', '10000', '0'],
  ] as const)(
    'mostra compra %s e atualiza o histórico',
    async (situacaoFinanceira, rotulo, pagoCentavos, saldoCentavos) => {
      abrirDireto();
      await render();
      await clicar('Registrar pagamento');
      if (situacaoFinanceira === 'Quitada') await clicar('Usar saldo total');
      else await campo('valor', '25,00');
      api.compra.mockReturnValue(
        of({ ...compra, situacaoFinanceira, pagoCentavos, saldoCentavos }),
      );
      api.pagamentos.mockReturnValue(
        of([
          {
            id,
            data: lancamento.data,
            meio: 'Pix',
            valorCentavos: pagoCentavos,
            referencia: 'Recibo',
          },
        ]),
      );
      await clicar('Confirmar');
      expect(api.enviar).toHaveBeenCalledWith(
        expect.objectContaining({
          tipo: 'pagar',
          id,
          corpo: expect.objectContaining({ valorCentavos: pagoCentavos }),
        }),
      );
      expect(texto()).toContain(rotulo);
      expect(texto()).toContain('Recibo');
      expect(!!botao('Registrar pagamento')).toBe(situacaoFinanceira === 'Parcial');
    },
  );

  it('mostra saldo desconhecido como valor a conferir e bloqueia pagamento', async () => {
    abrirDireto();
    api.compra.mockReturnValue(
      of({
        ...compra,
        situacaoFinanceira: 'ValorAConferir',
        totalCentavos: null,
        saldoCentavos: null,
      }),
    );
    await render();
    expect(texto()).toMatch(/Saldo a pagar\s*Valor a conferir/);
    expect(botao('Registrar pagamento')).toBeUndefined();
  });

  it('reconsulta o saldo após conflito e mostra a mensagem do servidor', async () => {
    abrirDireto();
    await render();
    await clicar('Registrar pagamento');
    await clicar('Usar saldo total');
    api.enviar.mockReturnValue(
      throwError(
        () => new HttpErrorResponse({ status: 409, error: { detail: 'Saldo alterado.' } }),
      ),
    );
    api.compra.mockReturnValue(
      of({ ...compra, situacaoFinanceira: 'Parcial', pagoCentavos: '7500', saldoCentavos: '2500' }),
    );
    await clicar('Confirmar');
    expect(texto()).toContain('Saldo alterado.');
    expect(texto()).toMatch(/Saldo a pagar\s*R\$ 25,00/);
    expect(api.compra).toHaveBeenCalledTimes(2);
  });

  it('bloqueia duplo clique, consulta após recarga e reenvia manualmente com a mesma chave e corpo', async () => {
    abrirDireto();
    const resposta = new Subject<LancamentoFinanceiro>();
    api.enviar.mockReturnValueOnce(resposta);
    await render();
    await clicar('Registrar pagamento');
    await campo('valor', '25');
    const confirmar = botao('Confirmar')!;
    confirmar.click();
    confirmar.click();
    expect(api.enviar).toHaveBeenCalledTimes(1);
    resposta.error(new HttpErrorResponse({ status: 0 }));
    const tentativa = api.enviar.mock.calls[0][0];
    fixture.destroy();
    await render();
    expect(api.compra).toHaveBeenCalledTimes(2);
    expect(api.enviar).toHaveBeenCalledTimes(1);
    await clicar('Reenviar tentativa');
    expect(api.enviar).toHaveBeenLastCalledWith(tentativa);
  });
});

describe('centavosFinanceiros', () => {
  it('aceita o limite de bigint SQL sem converter a Number', () => {
    expect(centavosFinanceiros('92.233.720.368.547.758,07')).toBe(9223372036854775807n);
  });
  it.each(['92.233.720.368.547.758,08', '1,001', '-1', '0', '1.23', '1.234.56', 'NaN'])(
    'rejeita %s',
    (valor) => {
      expect(centavosFinanceiros(valor)).toBeNull();
    },
  );
});
