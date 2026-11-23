import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { routes } from '../app.routes';
import { AuthService } from '../core/auth/auth.service';
import { CatalogoPublicoService } from './catalogo-publico.service';
import { StoreHeaderComponent } from './store-header.component';
import { MinhaContaComponent } from './minha-conta.component';

const api = '/api/clientes/me/perfil';
const perfil = {
  nomeCompleto: 'Cliente Teste', email: 'cliente@example.test', telefone: '+5554990000000',
  endereco: { cep: '95200000', uf: 'RS', cidade: 'Vacaria', bairro: 'Centro', logradouro: 'Rua do titular',
    numero: '12', complemento: 'Apto 2' },
};

describe('Minha conta', () => {
  let http: HttpTestingController;
  let autenticado: boolean;
  const conta = routes.find(r => r.path === '' && r.children)?.children?.find(r => r.path === 'minha-conta')!;

  beforeEach(() => {
    autenticado = true;
    TestBed.configureTestingModule({ providers: [
      provideHttpClient(), provideHttpClientTesting(),
      provideRouter([conta, ...['', 'login', 'pedidos', 'cotacoes-correios', 'produtos']
        .map(path => ({ path, component: StoreHeaderComponent }))]),
      { provide: AuthService, useValue: {
        obterSessao: () => of(autenticado ? { userId: 'cliente' } : null),
        logout: () => { autenticado = false; return of(undefined); },
      } },
      { provide: CatalogoPublicoService, useValue: { listarCategorias: () => of([]) } },
    ] });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('renderiza o resumo e oferece atalhos reais', async () => {
    const tela = await RouterTestingHarness.create('/minha-conta');
    expect(tela.routeNativeElement?.querySelector('[role="status"]')).not.toBeNull();
    http.expectOne(api).flush(perfil);
    tela.detectChanges();
    for (const texto of ['Cliente Teste', 'cliente@example.test', '+5554990000000', 'CEP 95200-000', 'Apto 2']) {
      expect(tela.routeNativeElement?.textContent).toContain(texto);
    }
    expect(tela.routeNativeElement?.textContent).not.toContain('CPF');
    expect([...tela.routeNativeElement!.querySelectorAll('nav a')].map(a => a.getAttribute('href')))
      .toEqual(['/pedidos', '/cotacoes-correios', '/produtos']);
    tela.routeNativeElement!.querySelector<HTMLAnchorElement>('nav a')!.click();
    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/pedidos'));
  });

  it('oferece nova tentativa sem bloquear perfis incompletos', async () => {
    const tela = await RouterTestingHarness.create('/minha-conta');
    http.expectOne(api).error(new ProgressEvent('error'));
    tela.detectChanges();
    expect(tela.routeNativeElement?.querySelector('[role="alert"]')?.textContent).toContain('Não foi possível');
    tela.routeNativeElement!.querySelector('button')!.click();
    http.expectOne(api).flush({ nomeCompleto: null, email: null, telefone: null, endereco: null });
    tela.detectChanges();
    expect(tela.routeNativeElement?.textContent).toContain('não tem um endereço padrão');
    expect(tela.routeNativeElement?.textContent).toContain('dados pessoais ainda não estão disponíveis');
  });

  it('trata sessão expirada com retorno para Minha conta', async () => {
    const tela = await RouterTestingHarness.create('/minha-conta');
    http.expectOne(api).flush(null, { status: 401, statusText: 'Unauthorized' });
    tela.detectChanges();
    expect(tela.routeNativeElement?.querySelector('[role="alert"]')?.textContent).toContain('sessão expirou');
    const href = tela.routeNativeElement!.querySelector('a')!.getAttribute('href')!;
    expect(TestBed.inject(Router).parseUrl(href).queryParams['destino']).toBe('/minha-conta');
  });

  it('protege a rota anônima e retorna ao login após logout', async () => {
    const tela = await RouterTestingHarness.create('/');
    const linksConta = () => tela.routeNativeElement!.querySelectorAll('a[href="/minha-conta"]');
    expect(linksConta()).toHaveLength(2);
    expect(tela.routeNativeElement!.querySelector('.store-mobile-nav a[href="/minha-conta"]')).not.toBeNull();
    tela.routeNativeElement!.querySelector<HTMLButtonElement>('.store-account__logout')!.click();
    await tela.navigateByUrl('/minha-conta');
    expect(linksConta()).toHaveLength(0);
    expect(TestBed.inject(Router).url).toMatch(/^\/login\?destino=/);
    http.expectNone(api);
  });

  it('cancela resposta pendente ao sair e consulta novamente ao voltar', async () => {
    const tela = await RouterTestingHarness.create('/minha-conta');
    const antiga = http.expectOne(api);
    await tela.navigateByUrl('/');
    expect(antiga.cancelled).toBe(true);
    await tela.navigateByUrl('/minha-conta', MinhaContaComponent);
    http.expectOne(api).flush({ ...perfil, nomeCompleto: 'Outra sessão' });
    tela.detectChanges();
    expect(tela.routeNativeElement?.textContent).toContain('Outra sessão');
    expect(tela.routeNativeElement?.textContent).not.toContain('Cliente Teste');
  });
});
