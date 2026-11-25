import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { of } from 'rxjs';
import { routes } from '../../app.routes';
import { AdminAuthService } from '../../core/admin-auth/admin-auth.service';
import { AdminPixComponent } from './admin-pix.component';

const url = '/api/admin/configuracoes/pix-manual';
const dados = {
  tipoChave: 'Email',
  chavePix: 'pix@example.invalid',
  nomeRecebedor: 'Loja de teste',
  instrucoesAdicionais: null,
};

describe('AdminPixComponent', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter(routes),
        { provide: AdminAuthService, useValue: { getCurrentSession: () => of(null) } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('abre formulário vazio e permite cadastrar e atualizar com CSRF', () => {
    const fixture = TestBed.createComponent(AdminPixComponent);
    http.expectOne({ method: 'GET', url }).flush(null, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();
    const view: HTMLElement = fixture.nativeElement;
    const campo = (id: string) => view.querySelector<HTMLInputElement>(id)!;
    const preencher = (id: string, valor: string, evento = 'input') => {
      const input = campo(id);
      input.value = valor;
      input.dispatchEvent(new Event(evento));
    };
    const salvar = () => view.querySelector('form')!.dispatchEvent(new Event('submit'));
    expect(view.textContent).toContain('ainda não tem uma configuração PIX');
    const feedback = view.querySelector('[role=status]')!;
    expect(feedback.classList.contains('sr-only')).toBe(true);
    expect(feedback.classList.contains('hidden')).toBe(false);
    expect(campo('#pix-chave').value).toBe('');
    expect(view.querySelector('label[for="pix-chave"]')).not.toBeNull();
    expect(Array.from(view.querySelectorAll('option'), (opcao) => opcao.value)).toEqual([
      '',
      'Aleatoria',
      'Email',
      'Telefone',
      'Cnpj',
    ]);

    preencher('#pix-tipo', 'Telefone', 'change');
    preencher('#pix-chave', '11999999999');
    salvar();
    fixture.detectChanges();
    expect(campo('#pix-chave').getAttribute('aria-invalid')).toBe('true');
    expect(view.querySelector('#pix-chave-erro')).not.toBeNull();
    http.expectNone('/api/auth/csrf');

    preencher('#pix-tipo', 'Email', 'change');
    preencher('#pix-chave', dados.chavePix);
    preencher('#pix-recebedor', dados.nomeRecebedor);
    fixture.detectChanges();
    expect(view.querySelector('[aria-labelledby="pix-previa"]')?.textContent).toContain('Alterações ainda não salvas.');
    expect(view.querySelector('[aria-labelledby="pix-previa"]')?.textContent).toContain(dados.chavePix);
    salvar();
    salvar();
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'token-teste' });
    const put = http.expectOne({ method: 'PUT', url });
    expect(put.request.headers.get('X-XSRF-TOKEN')).toBe('token-teste');
    expect(put.request.body).toEqual(dados);
    put.flush(dados);
    fixture.detectChanges();
    expect(view.querySelector('[role=status]')).toBe(feedback);
    expect(feedback.textContent).toContain('salva');
    expect(feedback.classList.contains('sr-only')).toBe(false);

    preencher('#pix-instrucoes', 'Instrução de teste');
    salvar();
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'outro-token' });
    const atualizacao = http.expectOne({ method: 'PUT', url });
    expect(atualizacao.request.body).toEqual({
      ...dados,
      instrucoesAdicionais: 'Instrução de teste',
    });
    atualizacao.flush(atualizacao.request.body);
  });

  it('consulta o valor persistido após perder a resposta sem disparar outro PUT', () => {
    const fixture = TestBed.createComponent(AdminPixComponent);
    http.expectOne({ method: 'GET', url }).flush(dados);
    fixture.detectChanges();
    const view: HTMLElement = fixture.nativeElement;
    view.querySelector('form')!.dispatchEvent(new Event('submit'));
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'token-teste' });
    http.expectOne({ method: 'PUT', url }).error(new ProgressEvent('error'));
    fixture.detectChanges();
    view.querySelector('form')!.dispatchEvent(new Event('submit'));
    http.expectNone({ method: 'PUT', url });
    [...view.querySelectorAll('button')].find(item => item.textContent.includes('Consultar configuração salva'))!.click();
    http.expectOne({ method: 'GET', url }).flush(dados);
    fixture.detectChanges();
    expect(view.textContent).toContain('Configuração atual carregada');
  });

  it('aceita CNPJ formatado, valida os dígitos e exibe a chave normalizada da API', () => {
    const fixture = TestBed.createComponent(AdminPixComponent);
    http.expectOne({ method: 'GET', url }).flush(dados);
    fixture.detectChanges();
    const view: HTMLElement = fixture.nativeElement;
    const tipo = view.querySelector<HTMLSelectElement>('#pix-tipo')!;
    const chave = view.querySelector<HTMLInputElement>('#pix-chave')!;
    const salvar = () => view.querySelector('form')!.dispatchEvent(new Event('submit'));
    tipo.value = 'Cnpj';
    tipo.dispatchEvent(new Event('change'));
    chave.value = '12.ABC.345/01DE-36';
    chave.dispatchEvent(new Event('input'));
    salvar();
    fixture.detectChanges();
    expect(view.querySelector('#pix-chave-erro')?.textContent).toContain('dígitos verificadores');
    http.expectNone('/api/auth/csrf');

    chave.value = '12.abc.345/01de-35';
    chave.dispatchEvent(new Event('input'));
    salvar();
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'token-teste' });
    const put = http.expectOne({ method: 'PUT', url });
    expect(put.request.body).toEqual({
      ...dados,
      tipoChave: 'Cnpj',
      chavePix: '12.abc.345/01de-35',
    });
    put.flush({ ...dados, tipoChave: 'Cnpj', chavePix: '12ABC34501DE35' });
    fixture.detectChanges();
    expect(chave.value).toBe('12ABC34501DE35');
  });

  it('carrega valores existentes e não replica dados de erros de validação', () => {
    const fixture = TestBed.createComponent(AdminPixComponent);
    http.expectOne({ method: 'GET', url }).flush(dados);
    fixture.detectChanges();
    const view: HTMLElement = fixture.nativeElement;
    expect(view.querySelector<HTMLInputElement>('#pix-chave')?.value).toBe(dados.chavePix);
    view.querySelector('form')!.dispatchEvent(new Event('submit'));
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'token-teste' });
    http.expectOne({ method: 'PUT', url }).flush(
      { errors: { chavePix: [dados.chavePix] } },
      {
        status: 400,
        statusText: 'Bad Request',
      },
    );
    fixture.detectChanges();
    expect(view.querySelector('[role=alert]')?.textContent).toContain('Confira o tipo');
    expect(view.querySelector('[role=alert]')?.textContent).not.toContain(dados.chavePix);
  });

  it.each([400, 401, 403])('trata uma recusa HTTP %i ao salvar', (status) => {
    const fixture = TestBed.createComponent(AdminPixComponent);
    http.expectOne(url).flush(dados);
    fixture.detectChanges();
    const view: HTMLElement = fixture.nativeElement;
    view.querySelector('form')!.dispatchEvent(new Event('submit'));
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'token-teste' });
    http.expectOne({ method: 'PUT', url }).flush(null, { status, statusText: 'Recusado' });
    fixture.detectChanges();
    const mensagens = {
      400: 'validação de segurança',
      401: 'Entre novamente',
      403: 'não tem permissão',
    };
    expect(view.querySelector('[role=alert]')?.textContent).toContain(
      mensagens[status as keyof typeof mensagens],
    );
    expect(view.textContent).not.toContain('Configuração PIX salva.');
  });

  it('redireciona quem não possui sessão administrativa pela rota real', async () => {
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/admin/configuracoes/pix-manual');
    expect(router.url).toBe('/admin/login');
    http.expectNone(url);
  });
});
