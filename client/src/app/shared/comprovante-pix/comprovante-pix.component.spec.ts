import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ComprovantesPixService } from '../../core/comprovantes-pix.service';
import { ComprovantePixComponent } from './comprovante-pix.component';

const id = '22d6d00f-ab3f-4252-a48d-81717dcba777';
const rota = `/api/pedidos/${id}/comprovante-pix`;
const rotaAdmin = `/api/admin/pedidos/${id}/comprovante-pix`;
const anexo = { id: 'a', pedidoId: id, versao: 'v1', enviadoEmUtc: '2026-11-21T12:00:00Z', tipo: 'application/pdf', tamanhoBytes: 20 };
const arquivo = () => new File(['PDF teste'], 'recibo.pdf', { type: 'application/pdf' });

describe('ComprovantePixComponent', () => {
  let fixture: ComponentFixture<ComprovantePixComponent>;
  let http: HttpTestingController;
  let view: HTMLElement;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  function abrir(existe = false, admin = false, podeEnviar = true, pendente = podeEnviar) {
    fixture = TestBed.createComponent(ComprovantePixComponent);
    fixture.componentRef.setInput('pedidoId', id);
    fixture.componentRef.setInput('admin', admin);
    fixture.componentRef.setInput('podeEnviar', podeEnviar);
    fixture.componentRef.setInput('pagamentoPendente', pendente);
    fixture.detectChanges();
    view = fixture.nativeElement;
    const req = http.expectOne({ method: 'GET', url: admin ? rotaAdmin : rota });
    if (existe) req.flush(anexo);
    else req.flush(null, { status: 404, statusText: 'Ausente' });
    fixture.detectChanges();
  }

  function selecionar(file: File) {
    const campo = view.querySelector('input[type=file]')!;
    Object.defineProperty(campo, 'files', { configurable: true, value: [file] });
    campo.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  function clicar(rotulo: string) {
    const botao = [...view.querySelectorAll('button')].find(b => b.textContent?.trim() === rotulo);
    expect(botao).toBeDefined();
    botao!.click();
    fixture.detectChanges();
  }

  function post() {
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'csrf' });
    const req = http.expectOne({ method: 'POST', url: rota });
    expect(req.request.headers.get('X-XSRF-TOKEN')).toBe('csrf');
    expect(req.request.headers.has('Content-Type')).toBe(false);
    return req;
  }

  it('envia opcionalmente e atualiza apenas o estado do anexo', () => {
    abrir();
    expect(view.textContent).toContain('Nenhum comprovante enviado');
    const file = arquivo();
    selecionar(file);
    expect(view.textContent).toContain('recibo.pdf');
    clicar('Enviar comprovante');
    const req = post();
    expect(req.request.body.get('arquivo')).toBe(file);
    expect(req.request.headers.has('X-Comprovante-Versao')).toBe(false);
    req.flush(anexo);
    fixture.detectChanges();
    http.expectOne(rota).flush(anexo);
    fixture.detectChanges();
    expect(view.textContent).toContain('aguardando conferência');
    http.expectNone(r => r.url.includes('confirmar-pagamento'));
  });

  it('rejeita formato inválido e mais de 5 MiB antes do POST', () => {
    abrir();
    selecionar(new File(['conteúdo'], 'imagem.png', { type: 'text/html' }));
    expect(view.textContent).toContain('Selecione um arquivo');
    selecionar(new File([new Uint8Array(5 * 1024 * 1024 + 1)], 'recibo.pdf', { type: 'application/pdf' }));
    expect(view.textContent).toContain('até 5 MiB');
    http.expectNone('/api/auth/csrf');
  });

  it('substitui com confirmação e versão, e permite cancelar antes do envio', () => {
    abrir(true);
    selecionar(arquivo());
    clicar('Substituir comprovante');
    clicar('Cancelar substituição');
    http.expectNone('/api/auth/csrf');
    selecionar(arquivo());
    clicar('Substituir comprovante');
    clicar('Confirmar substituição');
    const req = post();
    expect(req.request.headers.get('X-Comprovante-Versao')).toBe('v1');
    req.flush({ ...anexo, versao: 'v2' });
    fixture.detectChanges();
    http.expectOne(rota).flush({ ...anexo, versao: 'v2' });
  });

  it('recupera a resposta perdida com a mesma chave, versão e arquivo mesmo após perder elegibilidade', () => {
    abrir(true);
    const file = arquivo();
    selecionar(file);
    clicar('Substituir comprovante');
    clicar('Confirmar substituição');
    const primeira = post();
    primeira.error(new ProgressEvent('error'));
    fixture.componentRef.setInput('podeEnviar', false);
    fixture.detectChanges();
    expect(view.querySelector('input[type=file]')).toBeNull();
    clicar('Recuperar envio');
    const segunda = post();
    expect(segunda.request.headers.get('Idempotency-Key')).toBe(primeira.request.headers.get('Idempotency-Key'));
    expect(segunda.request.headers.get('X-Comprovante-Versao')).toBe('v1');
    expect(segunda.request.body.get('arquivo')).toBe(file);
    segunda.flush({ ...anexo, versao: 'v2' });
    fixture.detectChanges();
    http.expectOne(rota).flush({ ...anexo, versao: 'v2' });
    fixture.detectChanges();
    expect(view.textContent).toContain('Envio recuperado');
  });

  it('consulta a versão e solicita revalidação do pedido após 409', () => {
    abrir(true);
    const revalidar = vi.fn();
    fixture.componentInstance.revalidarPedido.subscribe(revalidar);
    selecionar(arquivo());
    clicar('Substituir comprovante');
    clicar('Confirmar substituição');
    post().flush(null, { status: 409, statusText: 'Conflito' });
    fixture.detectChanges();
    expect(revalidar).toHaveBeenCalledOnce();
    expect(view.querySelector('input[type=file]')).toBeNull();
    http.expectOne(rota).flush({ ...anexo, versao: 'v2' });
    fixture.detectChanges();
    expect(view.textContent).toContain('Confira a situação atual');
  });

  it.each([false, true])('faz download privado, ADMIN %s', admin => {
    abrir(true, admin, false);
    expect(view.querySelector('input[type=file]')).toBeNull();
    expect(view.textContent).not.toContain('aguardando conferência');
    if (admin) expect(view.textContent).toContain('não confirma o crédito bancário');
    const salvar = vi.spyOn(TestBed.inject(ComprovantesPixService), 'salvarDownload').mockImplementation(() => {});
    clicar('Baixar comprovante');
    const blob = new Blob(['teste'], { type: 'application/pdf' });
    const req = http.expectOne((admin ? rotaAdmin : rota) + '/conteudo');
    expect(req.request.responseType).toBe('blob');
    req.flush(blob);
    expect(salvar).toHaveBeenCalledWith(blob);
  });
});
