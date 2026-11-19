import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { MeusPedidosComponent } from './meus-pedidos.component';
import { PedidoConfirmacaoComponent } from './pedido-confirmacao.component';
import { ResumoPedidoCliente } from './pedidos.service';

const base = '/api/pedidos';
const id = '22d6d00f-ab3f-4252-a48d-81717dcba777';
const outro = '133cbbb0-09eb-43c4-8e62-94d639dd9e22';
const resumo: ResumoPedidoCliente = {
  id,
  situacao: 'AguardandoPagamento',
  criadoEmUtc: '2026-11-18T14:00:00Z',
  prazoPagamentoEmUtc: '2026-11-19T14:00:00Z',
  totalCentavos: '9223372036854775807',
};
const pagina = (numero: number, itens: ResumoPedidoCliente[], totalItens = itens.length) => ({
  itens,
  pagina: numero,
  itensPorPagina: 20,
  totalItens,
  totalPaginas: Math.ceil(totalItens / 20),
});

describe('Meus pedidos', () => {
  let http: HttpTestingController;
  const listar = (numero = 1) => http.expectOne({ method: 'GET', url: base + '?pagina=' + numero });

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'pedidos', component: MeusPedidosComponent },
          { path: 'pedidos/:id', component: PedidoConfirmacaoComponent },
        ]),
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.expectNone({ method: 'POST', url: base });
    http.verify();
  });

  it('apresenta os pedidos na ordem da API com estado, data e centavos exatos', async () => {
    const h = await RouterTestingHarness.create('/pedidos');
    expect(h.routeNativeElement!.querySelector('[role=status]')?.textContent).toContain(
      'Consultando',
    );
    listar().flush(pagina(1, [resumo, { ...resumo, id: outro, situacao: 'ProntoParaRetirada' }]));
    h.detectChanges();
    const view = h.routeNativeElement!;
    const itens = Array.from(view.querySelectorAll('ul li'), (li) => li.textContent);
    expect(itens[0]).toContain(id);
    expect(itens[0]).toContain('92.233.720.368.547.758,07');
    expect(itens[1]).toContain('Pronto para retirada');
    expect(view.querySelector('time')?.dateTime).toBe(resumo.criadoEmUtc);
  });

  it('distingue uma conta sem pedidos de uma sessão ausente', async () => {
    const h = await RouterTestingHarness.create('/pedidos');
    listar().flush(pagina(1, []));
    h.detectChanges();
    expect(h.routeNativeElement!.textContent).toContain('Você ainda não tem pedidos.');
    expect(h.routeNativeElement!.querySelector('[role=alert]')).toBeNull();
  });

  it.each([401, 403])('protege as informações quando a API responde %i', async (status) => {
    const h = await RouterTestingHarness.create('/pedidos');
    listar().flush(null, { status, statusText: 'Acesso recusado' });
    h.detectChanges();
    const view = h.routeNativeElement!;
    expect(view.querySelector('[role=alert]')).not.toBeNull();
    expect(view.querySelector('ul')).toBeNull();
    expect(!!view.querySelector('a[href="/login?retorno=pedidos"]')).toBe(status === 401);
  });

  it('recupera falha de rede e retira os dados da tela se a sessão expirar na paginação', async () => {
    const h = await RouterTestingHarness.create('/pedidos');
    listar().error(new ProgressEvent('error'));
    h.detectChanges();
    h.routeNativeElement!.querySelector('button')!.click();
    listar().flush(pagina(1, [resumo], 21));
    h.detectChanges();
    expect(h.routeNativeElement!.textContent).toContain(id);
    h.routeNativeElement!.querySelector<HTMLAnchorElement>('a[href="/pedidos?pagina=2"]')!.click();
    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/pedidos?pagina=2'));
    listar(2).flush(null, { status: 401, statusText: 'Unauthorized' });
    h.detectChanges();
    expect(h.routeNativeElement!.textContent).not.toContain(id);
    expect(h.routeNativeElement!.textContent).toContain('Entre na sua conta');
  });

  it('abre o detalhe e o PIX e retorna à página anterior da listagem', async () => {
    const h = await RouterTestingHarness.create('/pedidos?pagina=2');
    listar(2).flush(pagina(2, [resumo], 21));
    h.detectChanges();
    expect(h.routeNativeElement!.textContent).toContain('Página 2 de 2');
    h.routeNativeElement!.querySelector<HTMLAnchorElement>('ul a')!.click();
    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/pedidos/' + id + '?pagina=2'));
    const prazoPagamentoEmUtc = new Date(Date.now() + 60_000).toISOString();
    http.expectOne(base + '/' + id).flush({
      ...resumo,
      prazoPagamentoEmUtc,
      modalidadeEntrega: 'RetiradaLocal',
      itens: [
        {
          produtoId: outro,
          produtoVarianteId: outro,
          quantidade: 1,
          nomeProduto: 'Sabonete',
          nomeVariante: 'Lavanda',
          precoUnitarioCentavos: resumo.totalCentavos,
          subtotalCentavos: resumo.totalCentavos,
        },
      ],
    });
    h.detectChanges();
    http.expectOne(base + '/' + id + '/pagamento').flush({
      situacao: 'disponivel',
      valorCentavos: resumo.totalCentavos,
      prazoPagamentoEmUtc,
      pix: {
        tipoChave: 'Email',
        chavePix: 'pix@example.invalid',
        nomeRecebedor: 'Loja teste',
        instrucoesAdicionais: null,
      },
    });
    h.detectChanges();
    expect(h.routeNativeElement!.textContent).toContain('Copiar chave PIX');
    h.routeNativeElement!.querySelector<HTMLAnchorElement>('a[href="/pedidos?pagina=2"]')!.click();
    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/pedidos?pagina=2'));
    listar(2).flush(pagina(2, [resumo], 21));
  });

  it('preserva a recusa de um pedido fora da conta e não busca o PIX', async () => {
    const h = await RouterTestingHarness.create('/pedidos/' + outro);
    http.expectOne(base + '/' + outro).flush(null, { status: 404, statusText: 'Not Found' });
    h.detectChanges();
    expect(h.routeNativeElement!.textContent).toContain('Pedido não encontrado para esta conta');
    http.expectNone(base + '/' + outro + '/pagamento');
  });
});
