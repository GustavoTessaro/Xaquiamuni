import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { PedidoPixComponent } from './pedido-pix.component';

const id = '22d6d00f-ab3f-4252-a48d-81717dcba777';
const url = `/api/pedidos/${id}/pagamento`;
const pix = {
  tipoChave: 'Email',
  chavePix: 'pix@example.invalid',
  nomeRecebedor: 'Loja de teste',
  instrucoesAdicionais: 'Instrução de teste',
};

describe('PedidoPixComponent', () => {
  let http: HttpTestingController;
  let fixture: ComponentFixture<PedidoPixComponent>;
  let view: HTMLElement;
  let resposta: object;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(PedidoPixComponent);
    fixture.componentRef.setInput('pedidoId', id);
    fixture.componentRef.setInput('aguardandoPagamento', true);
    fixture.detectChanges();
    view = fixture.nativeElement;
    resposta = {
      situacao: 'disponivel',
      pix,
      valorCentavos: '9223372036854775807',
      prazoPagamentoEmUtc: new Date(Date.now() + 60_000).toISOString(),
    };
  });

  afterEach(() => http.verify());

  it.each([pix, { ...pix, tipoChave: 'Cnpj', chavePix: '12ABC34501DE35' }])(
    'mostra valor exato e copia somente a chave $tipoChave com feedback',
    async (chave) => {
      const clipboardOriginal = Object.getOwnPropertyDescriptor(navigator, 'clipboard');
      const writeText = vi.fn().mockResolvedValue(undefined);
      Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } });
      try {
        http.expectOne({ method: 'GET', url }).flush({ ...resposta, pix: chave });
        fixture.detectChanges();
        expect(view.textContent).toContain(chave.tipoChave === 'Cnpj' ? 'CNPJ' : 'E-mail');
        expect(view.textContent).toContain(chave.chavePix);
        expect(view.textContent).toContain('92.233.720.368.547.758,07');
        expect(view.textContent).toContain('Loja de teste');
        expect(view.textContent).toContain('manualmente');
        view.querySelector('button')!.click();
        await vi.waitFor(() => {
          fixture.detectChanges();
          expect(view.querySelector('[aria-live]')?.textContent).toContain('copiada');
        });
        expect(writeText).toHaveBeenCalledWith(chave.chavePix);
        writeText.mockRejectedValueOnce(new Error());
        view.querySelector('button')!.click();
        await vi.waitFor(() => {
          fixture.detectChanges();
          expect(view.querySelector('[aria-live]')?.textContent).toContain(
            'Não foi possível copiar',
          );
        });
      } finally {
        if (clipboardOriginal) Object.defineProperty(navigator, 'clipboard', clipboardOriginal);
        else Reflect.deleteProperty(navigator, 'clipboard');
      }
    },
  );

  it.each([
    ['configuracao_ausente', 'ainda não disponibilizou'],
    ['pagamento_indisponivel', 'não está disponível'],
  ])('mostra %s sem divulgar a chave ou oferecer cópia', (situacao, mensagem) => {
    http.expectOne(url).flush({ ...resposta, situacao, pix: null });
    fixture.detectChanges();
    expect(view.textContent).toContain(mensagem);
    expect(view.textContent).not.toContain(pix.chavePix);
    expect(view.querySelector('button')).toBeNull();
  });

  it.each([401, 403, 404])('trata HTTP %i sem mostrar dados PIX', (status) => {
    http.expectOne(url).flush(null, { status, statusText: 'Falha' });
    fixture.detectChanges();
    expect(view.querySelector('[role=alert]')).not.toBeNull();
    expect(view.textContent).not.toContain(pix.chavePix);
  });

  it('oculta a chave quando o prazo acaba com a página aberta', () => {
    vi.useFakeTimers();
    try {
      http
        .expectOne(url)
        .flush({ ...resposta, prazoPagamentoEmUtc: new Date(Date.now() + 1000).toISOString() });
      fixture.detectChanges();
      expect(view.textContent).toContain(pix.chavePix);
      vi.advanceTimersByTime(1000);
      fixture.detectChanges();
      expect(view.textContent).not.toContain(pix.chavePix);
      expect(view.querySelector('button')).toBeNull();
    } finally {
      vi.useRealTimers();
    }
  });

  it('recupera falha de rede consultando apenas as instruções', () => {
    http.expectOne(url).error(new ProgressEvent('error'));
    fixture.detectChanges();
    view.querySelector('button')!.click();
    fixture.detectChanges();
    http.expectOne({ method: 'GET', url }).flush(resposta);
    fixture.detectChanges();
    expect(view.textContent).toContain(pix.chavePix);
    http.expectNone({ method: 'POST', url: '/api/pedidos' });
  });
});
