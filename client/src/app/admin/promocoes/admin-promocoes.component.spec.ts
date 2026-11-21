import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { AdminAuthService } from '../../core/admin-auth/admin-auth.service';
import { CsrfService } from '../../core/security/csrf.service';
import { AdminPromocoesComponent } from './admin-promocoes.component';
import { Promocao } from './promocoes.service';

const rota = '/api/admin/promocoes';
const inativa: Promocao = {
  id: 'promo', produtoId: 'produto', escopo: 'TodasVariantes', tipo: 'Percentual',
  valor: '1250', inicioUtc: null, fimUtc: null, variantesIds: [], ativa: false, versao: 1,
};
const produto = { id: 'produto', nome: 'Sabonete', variantes: [{ id: 'variante', identificacao: 'Lavanda', ativa: true }] };
const preco = { regularCentavos: '2000', finalCentavos: '1750', economiaCentavos: '250', offPontosBase: 1250 };
const previa = {
  instanteUtc: '2026-11-21T12:00:00Z', conflitos: [],
  variantes: [{
    varianteId: 'variante', identificacao: 'Lavanda', ativa: true, abrangida: true,
    regularCentavos: '2000', precoProposto: preco, precoEfetivo: preco, propostaEfetiva: true,
    precoIncompativel: false, promocaoEfetivaId: null, especificaPrevalece: true,
  }],
};

describe('Promoções administrativas', () => {
  let f: ComponentFixture<AdminPromocoesComponent>;
  let http: HttpTestingController;
  const el = () => f.nativeElement as HTMLElement;
  const botao = (texto: string) => [...el().querySelectorAll('button')].find(b => b.textContent?.trim() === texto)!;
  const nome = (campo: string) => el().querySelector<HTMLInputElement | HTMLSelectElement>(`[name="${campo}"]`)!;

  async function preencher(campo: string, valor: string): Promise<void> {
    await f.whenStable();
    const controle = nome(campo);
    controle.value = valor;
    controle.dispatchEvent(new Event(controle.tagName === 'SELECT' ? 'change' : 'input', { bubbles: true }));
    await f.whenStable();
    f.detectChanges();
  }

  async function consultaPrevia() {
    let encontrados: ReturnType<HttpTestingController['match']> = [];
    await vi.waitFor(() => {
      f.detectChanges();
      encontrados = http.match(rota + '/previa');
      expect(encontrados).toHaveLength(1);
    });
    expect(encontrados[0].request.headers.get('X-XSRF-TOKEN')).toBe('csrf');
    return encontrados[0];
  }

  async function responderListaAtualizada(): Promise<void> {
    await vi.waitFor(() => {
      f.detectChanges();
      http.expectOne(r => r.url === rota && r.method === 'GET').flush([inativa]);
    });
  }

  beforeEach(async () => {
    TestBed.configureTestingModule({ providers: [
      provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
      { provide: CsrfService, useValue: { getRequestToken: () => of({ requestToken: 'csrf' }) } },
      { provide: AdminAuthService, useValue: { getCurrentSession: () => of({ userId: 'admin' }) } },
    ] });
    f = TestBed.createComponent(AdminPromocoesComponent);
    http = TestBed.inject(HttpTestingController);
    f.detectChanges();
    http.expectOne('/api/admin/produtos').flush([produto]);
    await f.whenStable();
    http.expectOne(rota + '?pagina=1').flush([inativa]);
    f.detectChanges();
  });

  afterEach(() => http.verify());

  it('lista e filtra por produto e cria rascunho percentual após prévia', async () => {
    expect(el().textContent).toContain('Inativa');
    const filtro = el().querySelector<HTMLSelectElement>('#lista-promocoes ~ label select')!;
    filtro.value = 'produto';
    filtro.dispatchEvent(new Event('change'));
    f.detectChanges();
    await f.whenStable();
    http.expectOne(r => r.url === rota && r.params.get('produtoId') === 'produto').flush([inativa]);
    botao('Nova promoção').click();
    f.detectChanges();
    await preencher('valor', '12,5');
    const simulada = await consultaPrevia();
    expect(simulada.request.body.configuracao).toMatchObject({ valor: '1250', escopo: 'TodasVariantes' });
    simulada.flush(previa);
    f.detectChanges();
    expect(el().textContent).toContain('desconto menor');
    botao('Salvar como inativa').click();
    await f.whenStable();
    const escrita = http.expectOne({ method: 'POST', url: rota });
    expect(escrita.request.body).toMatchObject({ ativa: false, configuracao: { valor: '1250' } });
    expect(escrita.request.headers.get('Idempotency-Key')).toMatch(/^[a-f0-9-]{36}$/i);
    escrita.flush(inativa);
    await responderListaAtualizada();
    (await consultaPrevia()).flush(previa);
  });

  it('converte preço fixo de uma variante e impede gravação incompatível', async () => {
    botao('Nova promoção').click();
    f.detectChanges();
    await preencher('produto', 'produto');
    await preencher('escopo', 'VariantesSelecionadas');
    await preencher('tipo', 'PrecoFixo');
    await preencher('valor', 'R$ 19,90');
    expect(el().textContent).toContain('Selecione pelo menos uma variante');
    el().querySelector<HTMLInputElement>('form input[type="checkbox"]')!.click();
    f.detectChanges();
    const req = await consultaPrevia();
    expect(req.request.body.configuracao).toMatchObject({ valor: '1990', variantesIds: ['variante'] });
    req.flush({ ...previa, variantes: [{ ...previa.variantes[0], precoProposto: null, precoIncompativel: true }] });
    f.detectChanges();
    expect(botao('Salvar como inativa').disabled).toBe(true);
    expect(el().textContent).toContain('menor que o preço regular');
  });

  it('exige revisão antes de ativar e bloqueia conflito de versão até recarregar', async () => {
    botao('Ver detalhes').click();
    http.expectOne(rota + '/promo').flush(inativa);
    await f.whenStable();
    f.detectChanges();
    expect(botao('Ativar após revisão').disabled).toBe(true);
    (await consultaPrevia()).flush(previa);
    f.detectChanges();
    botao('Ativar após revisão').click();
    await f.whenStable();
    const ativacao = http.expectOne({ method: 'PATCH', url: rota + '/promo/status' });
    expect(ativacao.request.body).toEqual({ ativa: true, versao: 1 });
    ativacao.flush({ codigo: 'versao_divergente' }, { status: 409, statusText: 'Conflict' });
    await vi.waitFor(() => {
      f.detectChanges();
      expect(el().textContent).toContain('Outra alteração foi salva');
    });
    expect(botao('Ativar após revisão')).toBeUndefined();
    botao('Recarregar promoção').click();
    http.expectOne(rota + '/promo').flush({ ...inativa, ativa: true, versao: 2 });
    await f.whenStable();
    f.detectChanges();
    botao('Desativar').click();
    f.detectChanges();
    http.expectNone(r => r.method === 'PATCH');
    botao('Confirmar desativação').click();
    await f.whenStable();
    const desativar = http.expectOne({ method: 'PATCH', url: rota + '/promo/status' });
    expect(desativar.request.body).toEqual({ ativa: false, versao: 2 });
    desativar.flush({ ...inativa, versao: 3 });
    await responderListaAtualizada();
    (await consultaPrevia()).flush(previa);
  });
});
