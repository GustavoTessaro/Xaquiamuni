import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { BehaviorSubject, of } from 'rxjs';
import { vi } from 'vitest';
import { routes } from '../app.routes';
import { AtendimentoService } from '../core/atendimento.service';
import { AuthService } from '../core/auth/auth.service';
import { CatalogoPublicoService } from './catalogo-publico.service';
import { StoreHeaderComponent } from './store-header.component';
import { MinhaContaComponent } from './minha-conta.component';

const api = '/api/clientes/me/perfil';
const versoes = { 'X-Perfil-Versao': '"perfil"', 'X-Endereco-Versao': '"endereco"' };
const perfil = {
  nomeCompleto: 'Cliente Teste', email: 'cliente@example.test', telefone: '+5554990000000',
  endereco: { cep: '95200000', uf: 'RS', cidade: 'Vacaria', bairro: 'Centro', logradouro: 'Rua do titular',
    numero: '12', complemento: 'Apto 2' },
};

describe('Minha conta', () => {
  let http: HttpTestingController;
  let autenticado: boolean;
  let canais: BehaviorSubject<{ whatsApp: string | null; emailComercial: string | null }>;
  const conta = routes.find(r => r.path === '' && r.children)?.children?.find(r => r.path === 'minha-conta')!;

  beforeEach(() => {
    autenticado = true;
    canais = new BehaviorSubject<{ whatsApp: string | null; emailComercial: string | null }>({ whatsApp: null, emailComercial: null });
    TestBed.configureTestingModule({ providers: [
      provideHttpClient(), provideHttpClientTesting(),
      provideRouter([conta, ...['', 'login', 'pedidos', 'cotacoes-correios', 'produtos']
        .map(path => ({ path, component: StoreHeaderComponent }))]),
      { provide: AuthService, useValue: {
        obterSessao: () => of(autenticado ? { userId: 'cliente' } : null),
        logout: () => { autenticado = false; return of(undefined); },
      } },
      { provide: AtendimentoService, useValue: { contato: () => canais } },
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
      .toEqual(['/pedidos', '/cotacoes-correios', '#conta-dados-pessoais', '#conta-endereco-padrao']);
    expect(tela.routeNativeElement?.textContent).toContain('Olá, Cliente Teste!');
    tela.routeNativeElement!.querySelector<HTMLAnchorElement>('nav a')!.click();
    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/pedidos'));
  });

  it('oferece cadastro de endereço e omite canais comerciais indisponíveis', async () => {
    const tela = await RouterTestingHarness.create('/minha-conta');
    http.expectOne(api).flush({ ...perfil, endereco: null }, { headers: versoes });
    tela.detectChanges();
    const elemento = tela.routeNativeElement!;
    expect(elemento.querySelector('[href="#conta-contato"]')).toBeNull();
    expect(elemento.querySelector('[aria-label="Cadastrar endereço padrão"]')).not.toBeNull();
    canais.next({ whatsApp: null, emailComercial: 'contato@example.test' });
    tela.detectChanges();
    expect(elemento.querySelector('[href="#conta-contato"]')).not.toBeNull();
    expect(elemento.querySelector('[href="mailto:contato@example.test"]')).not.toBeNull();
    canais.next({ whatsApp: null, emailComercial: null });
    tela.detectChanges();
    expect(elemento.querySelector('[href="#conta-contato"]')).toBeNull();
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

  it('edita, cancela e salva somente os dados pessoais permitidos', async () => {
    const tela = await RouterTestingHarness.create('/minha-conta');
    const elemento = tela.routeNativeElement!;
    http.expectOne(api).flush(perfil, { headers: versoes });
    tela.detectChanges();
    const editar = () => {
      elemento.querySelector<HTMLButtonElement>('[aria-label="Editar dados pessoais"]')!.click();
      tela.detectChanges();
    };
    editar();
    expect(elemento.querySelector<HTMLInputElement>('#conta-nome')?.value).toBe(perfil.nomeCompleto);
    expect(elemento.querySelector('input[type="email"]')).toBeNull();
    elemento.querySelector<HTMLButtonElement>('form button[type="button"]')!.click();
    tela.detectChanges();
    expect(elemento.querySelector('form')).toBeNull();

    editar();
    const nome = elemento.querySelector<HTMLInputElement>('#conta-nome')!;
    const digitar = (valor: string) => { nome.value = valor; nome.dispatchEvent(new Event('input')); };
    const salvar = () => elemento.querySelector('form')!.dispatchEvent(new Event('submit'));
    digitar('');
    salvar();
    tela.detectChanges();
    expect(elemento.querySelector('#conta-nome-erro')?.textContent).toContain('Informe o nome');
    http.expectNone('/api/auth/csrf');
    digitar('Nome Alterado');
    salvar();
    salvar();
    tela.detectChanges();
    expect(elemento.querySelector('[role="status"]')?.textContent).toContain('Salvando');
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'csrf' });
    const escrita = http.expectOne({ method: 'PUT', url: api });
    expect(escrita.request.headers.get('If-Match')).toBe('"perfil"');
    expect(escrita.request.headers.get('X-XSRF-TOKEN')).toBe('csrf');
    expect(escrita.request.body).toEqual({ nomeCompleto: 'Nome Alterado', telefone: perfil.telefone });
    escrita.flush({ ...perfil, nomeCompleto: 'Nome Alterado' }, { headers: versoes });
    tela.detectChanges();
    expect(elemento.textContent).toContain('Nome Alterado');
    expect(elemento.textContent).toContain('Alterações salvas');
    expect(elemento.querySelector('nav a[href="/pedidos"]')).not.toBeNull();
  });

  it('troca a edição, trata erro de rede e oferece recarregar após conflito de endereço', async () => {
    const tela = await RouterTestingHarness.create('/minha-conta');
    const elemento = tela.routeNativeElement!;
    http.expectOne(api).flush(perfil, { headers: versoes });
    tela.detectChanges();
    for (const rotulo of ['Editar dados pessoais', 'Editar endereço padrão']) {
      elemento.querySelector<HTMLButtonElement>('[aria-label="' + rotulo + '"]')!.click();
      tela.detectChanges();
    }
    expect(elemento.querySelector('#conta-nome')).toBeNull();
    expect(elemento.querySelector<HTMLInputElement>('#conta-cep')?.value).toBe('95200000');
    for (const status of [0, 409]) {
      elemento.querySelector('form')!.dispatchEvent(new Event('submit'));
      http.expectOne('/api/auth/csrf').flush({ requestToken: 'csrf' });
      const escrita = http.expectOne({ method: 'PUT', url: '/api/clientes/me/endereco-padrao' });
      expect(escrita.request.headers.get('If-Match')).toBe('"endereco"');
      if (status === 0) escrita.error(new ProgressEvent('error'));
      else escrita.flush(null, { status, statusText: 'Conflict' });
      tela.detectChanges();
      expect(elemento.querySelector('[role="alert"]')?.textContent)
        .toContain(status === 0 ? 'Não foi possível confirmar' : 'Os dados mudaram');
    }
    expect(elemento.querySelector<HTMLButtonElement>('button[type="submit"]')?.disabled).toBe(true);
    [...elemento.querySelectorAll('button')].find(x => x.textContent === 'Recarregar dados')!.click();
    http.expectOne(api).flush({ ...perfil, endereco: null }, { headers: versoes });
    tela.detectChanges();
    expect(elemento.textContent).toContain('não tem um endereço padrão');
    expect(elemento.querySelector('form')).toBeNull();
  });

  it('preenche o endereço por CEP mantendo número, complemento e a versão de endereço', async () => {
    const tela = await RouterTestingHarness.create('/minha-conta');
    const elemento = tela.routeNativeElement!;
    http.expectOne(api).flush(perfil, { headers: versoes });
    tela.detectChanges();
    elemento.querySelector<HTMLButtonElement>('[aria-label="Editar endereço padrão"]')!.click();
    tela.detectChanges();
    elemento.querySelector<HTMLButtonElement>('app-consulta-cep button')!.click();
    http.expectOne('/api/cep/95200000').flush({ uf: 'RS', cidade: 'Vacaria', logradouro: 'Rua assistida' });
    tela.detectChanges();
    expect(elemento.querySelector<HTMLInputElement>('#conta-numero')?.value).toBe('12');
    expect(elemento.querySelector<HTMLInputElement>('#conta-complemento')?.value).toBe('Apto 2');
    expect(elemento.querySelector<HTMLInputElement>('#conta-bairro')?.value).toBe('Centro');
    elemento.querySelector('form')!.dispatchEvent(new Event('submit'));
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'csrf' });
    const escrita = http.expectOne({ method: 'PUT', url: '/api/clientes/me/endereco-padrao' });
    expect(escrita.request.headers.get('If-Match')).toBe('"endereco"');
    expect(escrita.request.body).toEqual({ ...perfil.endereco, logradouro: 'Rua assistida' });
    escrita.flush({ ...perfil, endereco: escrita.request.body }, { headers: versoes });
    tela.detectChanges();
    expect(elemento.querySelector('address')?.textContent).toContain('Rua assistida');
  });

  it('mostra validação do servidor e remove os dados se a sessão expirar durante a gravação', async () => {
    const tela = await RouterTestingHarness.create('/minha-conta');
    const elemento = tela.routeNativeElement!;
    http.expectOne(api).flush(perfil, { headers: versoes });
    tela.detectChanges();
    elemento.querySelector<HTMLButtonElement>('[aria-label="Editar dados pessoais"]')!.click();
    tela.detectChanges();

    const enviar = () => {
      elemento.querySelector('form')!.dispatchEvent(new Event('submit'));
      http.expectOne('/api/auth/csrf').flush({ requestToken: 'csrf' });
      return http.expectOne({ method: 'PUT', url: api });
    };
    enviar().flush({ errors: { telefone: ['O telefone brasileiro informado é inválido.'] } },
      { status: 400, statusText: 'Bad Request' });
    tela.detectChanges();
    expect(elemento.querySelector('#conta-telefone-erro')?.textContent).toContain('telefone brasileiro');
    expect(elemento.querySelector('#conta-telefone')?.getAttribute('aria-invalid')).toBe('true');

    enviar().flush(null, { status: 401, statusText: 'Unauthorized' });
    tela.detectChanges();
    expect(elemento.querySelector('[role="alert"]')?.textContent).toContain('sessão expirou');
    expect(elemento.textContent).not.toContain(perfil.email);
    expect(elemento.querySelector('form')).toBeNull();
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
