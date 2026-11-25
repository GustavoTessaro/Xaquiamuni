import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { of } from 'rxjs';
import { AtendimentoService } from '../core/atendimento.service';
import { StoreFooterComponent } from './store-footer.component';
import { StoreCustomerSession } from './store-customer-session.service';

@Component({ template: '' })
class DestinoTeste {}

describe('Rodapé da loja', () => {
  const sessao = '/api/auth/me';

  function criar() {
    TestBed.configureTestingModule({ providers: [
      provideHttpClient(), provideHttpClientTesting(),
      provideRouter([{ path: '', component: DestinoTeste }, { path: 'produtos', component: DestinoTeste }]),
      { provide: AtendimentoService, useValue: { contato: () => of({ whatsApp: null, emailComercial: null }) } },
    ] });
    const fixture = TestBed.createComponent(StoreFooterComponent);
    fixture.detectChanges();
    const elemento: HTMLElement = fixture.nativeElement;
    return {
      fixture, elemento, http: TestBed.inject(HttpTestingController),
      conta: () => elemento.querySelector('nav[aria-label="Acesso à conta"]')!,
    };
  }

  it('resolve sem piscar ações, atualiza a navegação e confirma logout com CSRF', async () => {
    const { fixture, conta, http } = criar();
    expect(conta().querySelector('a, button')).toBeNull();
    http.expectOne(sessao).flush(null, { status: 401, statusText: 'Unauthorized' });
    fixture.detectChanges();
    expect(conta().textContent).toContain('Criar conta');
    expect(conta().querySelector('button')).toBeNull();

    const router = TestBed.inject(Router);
    await router.navigateByUrl('/produtos');
    http.expectOne(sessao).flush({ userId: 'cliente' });
    fixture.detectChanges();
    expect(Array.from(conta().querySelectorAll('a'), a => a.getAttribute('href')))
      .toEqual(['/minha-conta', '/pedidos', '/cotacoes-correios']);
    expect(conta().textContent).not.toContain('Criar conta');
    const sair = conta().querySelector('button')!;
    sair.click();
    sair.click();
    http.expectOne('/api/auth/csrf').flush({ requestToken: 'csrf' });
    const req = http.expectOne({ method: 'POST', url: '/api/auth/logout' });
    expect(req.request.headers.get('X-XSRF-TOKEN')).toBe('csrf');
    req.flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();
    expect(conta().querySelector('a[href="/minha-conta"]')).toBeNull();
    await fixture.whenStable();
    http.expectOne(sessao).flush(null, { status: 401, statusText: 'Unauthorized' });
    fixture.detectChanges();
    expect(conta().textContent).toContain('Entrar');
    http.verify();
  });

  it('atualiza uma sessão expirada e mantém ADMIN como visitante da loja', async () => {
    const { fixture, conta, http } = criar();
    http.expectOne(sessao).flush({ userId: 'cliente' });
    fixture.detectChanges();
    expect(conta().textContent).toContain('Sair');
    await TestBed.inject(Router).navigateByUrl('/produtos');
    http.expectOne(sessao).flush(null, { status: 401, statusText: 'Unauthorized' });
    fixture.detectChanges();
    expect(conta().textContent).toContain('Entrar');
    expect(conta().querySelector('a[href="/minha-conta"]')).toBeNull();

    await TestBed.inject(Router).navigateByUrl('/');
    http.expectOne(sessao).flush(null, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();
    expect(conta().textContent).toContain('Criar conta');
    expect(conta().querySelector('button')).toBeNull();
    expect(TestBed.inject(StoreCustomerSession).session()).toBeNull();
    http.verify();
  });
});
