import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { HistoricoRastreamentoComponent } from './historico-rastreamento.component';

describe('HistoricoRastreamentoComponent', () => {
  const base = '/api/admin/pedidos/';
  const rota = (id: string) => base + id + '/historico-rastreamento';
  const evento = {
    codigoAnterior: null, codigoNovo: 'AB123456789BR', realizadaEmUtc: '2026-11-23T10:30:00Z',
    administradorId: '12345678-1234-4321-9876-123456789abc',
  };
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  function abrir() {
    const fixture = TestBed.createComponent(HistoricoRastreamentoComponent);
    fixture.componentRef.setInput('pedidoId', 'pedido-a');
    fixture.detectChanges();
    return fixture;
  }

  it('mostra carregamento, códigos, horário e autor auditado', () => {
    const fixture = abrir();
    expect(fixture.nativeElement.querySelector('[role="status"]').textContent).toContain('Carregando histórico');
    const req = http.expectOne(rota('pedido-a'));
    expect(req.request.method).toBe('GET');
    expect(req.request.headers.has('If-Match')).toBe(false);
    expect(req.request.headers.has('X-XSRF-TOKEN')).toBe(false);
    req.flush([evento]);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Não informado');
    expect(fixture.nativeElement.textContent).toContain(evento.codigoNovo);
    expect(fixture.nativeElement.textContent).toContain(evento.administradorId);
    expect(fixture.nativeElement.querySelector('time').getAttribute('datetime')).toBe(evento.realizadaEmUtc);
  });

  it('permite tentar novamente após falha de rede e mostra histórico vazio', () => {
    const fixture = abrir();
    http.expectOne(rota('pedido-a')).error(new ProgressEvent('error'));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain('Não foi possível');
    fixture.nativeElement.querySelector('button').click();
    fixture.detectChanges();
    http.expectOne(rota('pedido-a')).flush([]);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Nenhuma correção');
  });

  it.each([[401, 'Sessão expirada'], [403, 'não tem acesso'], [404, 'Pedido não encontrado']])(
    'distingue a falha HTTP %i',
    (status, mensagem) => {
      const fixture = abrir();
      http.expectOne(rota('pedido-a')).flush(null, { status, statusText: 'Erro' });
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(mensagem);
    },
  );

  it('cancela a consulta anterior na troca de pedido e na saída da página', () => {
    const fixture = abrir();
    const primeira = http.expectOne(rota('pedido-a'));
    fixture.componentRef.setInput('pedidoId', 'pedido-b');
    fixture.detectChanges();
    expect(primeira.cancelled).toBe(true);
    const segunda = http.expectOne(rota('pedido-b'));
    expect(fixture.nativeElement.querySelector('[role="status"]')).not.toBeNull();
    fixture.destroy();
    expect(segunda.cancelled).toBe(true);
  });
});
