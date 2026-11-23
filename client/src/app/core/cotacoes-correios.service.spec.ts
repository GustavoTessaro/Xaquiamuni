import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { CotacoesCorreiosService, erroPostal, type SolicitacaoPostal } from './cotacoes-correios.service';
import { AuthService } from './auth/auth.service';
import { CsrfService } from './security/csrf.service';

describe('CotacoesCorreiosService', () => {
  let service: CotacoesCorreiosService;
  let http: HttpTestingController;
  const corpo: SolicitacaoPostal = {
    itens: [{ produtoId: 'produto', produtoVarianteId: 'variante', quantidade: 2 }],
    endereco: { cep: '95200000', uf: 'RS', cidade: 'Vacaria', bairro: 'Centro', logradouro: 'Rua sintética', numero: '12', complemento: null },
  };

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [
      provideHttpClient(), provideHttpClientTesting(),
      { provide: AuthService, useValue: { obterSessao: () => of({ userId: 'titular' }) } },
      { provide: CsrfService, useValue: { getRequestToken: () => of({ requestToken: 'csrf' }) } },
    ] });
    service = TestBed.inject(CotacoesCorreiosService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('envia e recupera a solicitação usando os mesmos itens, endereço e chave', () => {
    const chave = crypto.randomUUID();
    const primeira = service.solicitar(corpo, chave);
    primeira.subscribe({ error: () => undefined });
    const original = http.expectOne('/api/cotacoes-correios');
    expect(original.request.method).toBe('POST');
    expect(original.request.headers.get('X-XSRF-TOKEN')).toBe('csrf');
    expect(original.request.headers.get('Idempotency-Key')).toBe(chave);
    expect(original.request.body).toEqual(corpo);
    original.error(new ProgressEvent('error'));

    service.solicitar(corpo, chave).subscribe(resposta => expect(resposta.id).toBe('cotacao'));
    const recuperacao = http.expectOne('/api/cotacoes-correios');
    expect(recuperacao.request.body).toEqual(original.request.body);
    expect(recuperacao.request.headers.get('Idempotency-Key')).toBe(chave);
    recuperacao.flush({ id: 'cotacao' });
  });

  it('separa a listagem ADMIN e obtém o ETag no detalhe do titular', () => {
    service.listar(true, 2, 'AguardandoAnalise').subscribe();
    http.expectOne('/api/admin/cotacoes-correios?pagina=2&situacao=AguardandoAnalise').flush({ itens: [] });
    service.detalhe(false, 'cotacao').subscribe(resposta => expect(resposta.headers.get('ETag')).toBe('"3"'));
    http.expectOne('/api/cotacoes-correios/cotacao').flush({}, { headers: { ETag: '"3"' } });
  });

  it.each([
    [false, 'aceitar', { revisao: 2 }],
    [false, 'recusar', { revisao: 2 }],
    [false, 'cancelar', { motivo: 'Desistência' }],
    [true, 'propostas', { servico: 'PAC', freteCentavos: '1290', prazoDias: 5, pesoTotalGramas: 200 }],
  ])('envia revisão, CSRF e chave para a transição %s %s', (admin, acao, dados) => {
    const chave = crypto.randomUUID();
    service.operar(admin, 'cotacao', acao, dados, '"3"', chave).subscribe({ error: erro => {
      expect(erroPostal(erro)).toContain('versão da proposta mudou');
    } });
    const req = http.expectOne(`/api/${admin ? 'admin/' : ''}cotacoes-correios/cotacao/${acao}`);
    expect(req.request.method).toBe('POST');
    expect(req.request.headers.get('If-Match')).toBe('"3"');
    expect(req.request.headers.get('X-XSRF-TOKEN')).toBe('csrf');
    expect(req.request.headers.get('Idempotency-Key')).toBe(chave);
    expect(req.request.body).toEqual(dados);
    req.flush({ codigo: 'revisao_desatualizada' }, { status: 409, statusText: 'Conflict' });
  });

  it('consulta a revisão sem enviar itens, destino ou preços do navegador', () => {
    service.revisar('cotacao').subscribe({ error: erro => expect(erroPostal(erro)).toContain('expirou') });
    const req = http.expectOne('/api/checkout/cotacoes-correios/cotacao/revisar');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toBeNull();
    expect(req.request.headers.get('X-XSRF-TOKEN')).toBe('csrf');
    req.flush({ codigo: 'cotacao_correios_expirada' }, { status: 409, statusText: 'Conflict' });
  });
});

