import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AdminTemasComponent } from './admin-temas.component';

describe('publicação administrativa de temas', () => {
  const url = '/api/admin/loja/tema';
  const original = { chave: 'original', revisao: 0, publicadoEmUtc: null, administradorId: null };
  const temas = [
    { chave: 'original', nome: 'Original', descricao: 'Identidade visual', disponivel: true },
    { chave: 'natal', nome: 'Natal', descricao: 'Tema sazonal', disponivel: false },
  ];

  function montar() {
    TestBed.configureTestingModule({
      imports: [AdminTemasComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    const fixture = TestBed.createComponent(AdminTemasComponent);
    const http = TestBed.inject(HttpTestingController);
    const tela = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    http.expectOne(url).flush({ publicacao: original, temas });
    fixture.detectChanges();
    const clicar = (rotulo: string) => {
      const botao = Array.from(tela.querySelectorAll('button')).find(b => b.textContent?.trim() === rotulo);
      expect(botao).toBeDefined();
      botao!.click();
      fixture.detectChanges();
    };
    return { fixture, http, tela, clicar };
  }

  it('exige confirmação explícita e preserva o UUID e corpo quando o resultado é incerto', () => {
    const { fixture, http, tela, clicar } = montar();
    expect(tela.textContent).toContain('Em preparação');
    expect(tela.textContent).not.toContain('Publicar Natal');
    clicar('Publicar Original');
    http.expectNone('/api/auth/csrf');
    clicar('Confirmar publicação');
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'csrf' });
    const primeira = http.expectOne(url);
    expect(primeira.request.method).toBe('PUT');
    expect(primeira.request.headers.get('X-XSRF-TOKEN')).toBe('csrf');
    expect(primeira.request.body).toEqual({ chave: 'original', revisaoEsperada: 0 });
    const uuid = primeira.request.headers.get('Idempotency-Key');
    expect(uuid).toMatch(/^[a-f0-9-]{36}$/);
    primeira.error(new ProgressEvent('error'));
    fixture.detectChanges();

    clicar('Confirmar publicação');
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'csrf' });
    const repetida = http.expectOne(url);
    expect(repetida.request.headers.get('Idempotency-Key')).toBe(uuid);
    expect(repetida.request.body).toEqual(primeira.request.body);
    repetida.flush({ ...original, revisao: 1 });
    fixture.detectChanges();
    expect(tela.textContent).toContain('Revisão 1');
    expect(tela.textContent).toContain('Tema publicado na loja.');
    http.verify();
  });

  it('bloqueia nova publicação após conflito até consultar a revisão atual', () => {
    const { fixture, http, tela, clicar } = montar();
    clicar('Publicar Original');
    clicar('Confirmar publicação');
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'csrf' });
    http.expectOne(url).flush(null, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();
    expect(tela.textContent).toContain('A publicação mudou');
    expect(tela.textContent).not.toContain('Confirmar publicação');
    clicar('Consultar publicação');
    http.expectOne(url).flush({ publicacao: { ...original, revisao: 3 }, temas });
    fixture.detectChanges();
    expect(tela.textContent).toContain('Revisão 3');
    http.verify();
  });
});
