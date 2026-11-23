import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { AdminAuthService } from '../../core/admin-auth/admin-auth.service';
import { CsrfService } from '../../core/security/csrf.service';
import { AdminPromocoesFreteComponent } from './admin-promocoes-frete.component';
import { CampanhaFrete } from './promocoes-frete.service';

const rota = '/api/admin/promocoes-frete';
const cadastro: CampanhaFrete = { id: 'campanha', nome: 'Frete de teste', tipo: 'Percentual',
  valor: '1250', modalidades: ['EntregaPropria'], subtotalMinimoCentavos: '0',
  inicioUtc: null, fimUtc: null, ativa: false, versao: 1 };

describe('Promoções administrativas de frete', () => {
  let fixture: ComponentFixture<AdminPromocoesFreteComponent>;
  let http: HttpTestingController;
  const tela = () => fixture.nativeElement as HTMLElement;
  const botao = (texto: string) => [...tela().querySelectorAll('button')].find(e => e.textContent.trim() === texto)!;

  async function atualizar(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function preencher(campo: string, valor: string): Promise<void> {
    await atualizar();
    const input = tela().querySelector<HTMLInputElement | HTMLSelectElement>('[name="' + campo + '"]')!;
    input.value = valor;
    input.dispatchEvent(new Event(input.tagName === 'SELECT' ? 'change' : 'input', { bubbles: true }));
    await atualizar();
  }

  function listar(itens: CampanhaFrete[]) {
    http.expectOne(rota + '?pagina=1').flush({ itens, temProxima: false });
    fixture.detectChanges();
  }

  beforeEach(async () => {
    TestBed.configureTestingModule({ providers: [
      provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
      { provide: CsrfService, useValue: { getRequestToken: () => of({ requestToken: 'csrf' }) } },
      { provide: AdminAuthService, useValue: { getCurrentSession: () => of({ userId: 'admin' }) } },
    ] });
    fixture = TestBed.createComponent(AdminPromocoesFreteComponent);
    http = TestBed.inject(HttpTestingController);
    await atualizar();
  });

  afterEach(() => http.verify());

  it('exibe carregamento, trata erro de rede e lista campanhas', async () => {
    expect(tela().textContent).toContain('Carregando campanhas');
    http.expectOne(rota + '?pagina=1').error(new ProgressEvent('error'));
    fixture.detectChanges();
    expect(tela().querySelector('[role=alert]')?.textContent).toContain('Não foi possível carregar');
    botao('Tentar novamente').click();
    await atualizar();
    listar([cadastro]);
    expect(tela().textContent).toContain('Frete de teste');
    expect(tela().textContent).toContain('12,50%');
    expect(tela().textContent).toContain('Entrega própria');
  });

  it('valida e cria campanha usando centavos e CSRF', async () => {
    listar([]);
    botao('Nova campanha').click();
    await preencher('nome', 'Frete de teste');
    await preencher('tipo', 'ValorFixo');
    await preencher('valor', '12,50');
    botao('Salvar campanha').click();
    fixture.detectChanges();
    expect(tela().querySelector('[role=alert]')?.textContent).toContain('pelo menos uma modalidade');
    http.expectNone({ url: rota, method: 'POST' });
    tela().querySelector<HTMLInputElement>('form input[type=checkbox]')!.click();
    await atualizar();
    botao('Salvar campanha').click();
    const escrita = http.expectOne({ url: rota, method: 'POST' });
    expect(escrita.request.body).toMatchObject({ valor: '1250', subtotalMinimoCentavos: '0', modalidades: ['EntregaPropria'] });
    expect(escrita.request.headers.get('X-XSRF-TOKEN')).toBe('csrf');
    escrita.flush({ ...cadastro, tipo: 'ValorFixo' });
    await atualizar();
    listar([{ ...cadastro, tipo: 'ValorFixo' }]);
    expect(tela().textContent).toContain('Campanha salva');
  });

  it('edita com If-Match e recarrega a revisão após conflito', async () => {
    listar([cadastro]);
    botao('Editar').click();
    http.expectOne(rota + '/campanha').flush(cadastro);
    await preencher('nome', 'Nome atualizado');
    botao('Salvar campanha').click();
    const escrita = http.expectOne({ url: rota + '/campanha', method: 'PUT' });
    expect(escrita.request.headers.get('If-Match')).toBe('"1"');
    expect(escrita.request.body.nome).toBe('Nome atualizado');
    escrita.flush(null, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();
    expect(botao('Salvar campanha').disabled).toBe(true);
    botao('Recarregar dados').click();
    http.expectOne(rota + '/campanha').flush({ ...cadastro, nome: 'Outra edição', versao: 2 });
    await atualizar();
    listar([{ ...cadastro, versao: 2 }]);
    expect(tela().querySelector<HTMLInputElement>('input[name=nome]')?.value).toBe('Outra edição');
  });

  it('ativa e inativa usando a revisão atual', async () => {
    listar([cadastro]);
    for (const [acao, versao] of [['Ativar', 1], ['Inativar', 2]] as const) {
      botao(acao).click();
      const escrita = http.expectOne({ url: rota + '/campanha/status', method: 'PATCH' });
      expect(escrita.request.headers.get('If-Match')).toBe('"' + versao + '"');
      expect(escrita.request.body).toEqual({ ativa: acao === 'Ativar' });
      const resposta = { ...cadastro, ativa: acao === 'Ativar', versao: versao + 1 };
      escrita.flush(resposta);
      await atualizar();
      listar([resposta]);
    }
    expect(tela().textContent).toContain('Campanha inativada');
  });
});
