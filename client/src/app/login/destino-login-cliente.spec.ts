import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { convertToParamMap, DefaultUrlSerializer, provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { AuthService } from '../core/auth/auth.service';
import { destinoLoginCliente, validarDestinoLoja } from '../core/auth/destino-login-cliente';
import { CatalogoPublicoService } from '../public-store/catalogo-publico.service';
import { StoreHeaderComponent } from '../public-store/store-header.component';
import { LoginComponent } from './login.component';

@Component({ imports: [StoreHeaderComponent], template: '<app-store-header />' })
class LojaTeste {}

const id = '20000000-0000-4000-8000-000000000001';
const busca = '/produtos?busca=vela%20arom%C3%A1tica&categoriaId=' + id + '#catalogo';
const destinos = ['/produtos/' + id, '/carrinho', '/minha-conta', busca, '/pedidos/' + id + '?pagina=2', '/cotacoes-correios/' + id];

describe('retorno do login do cliente', () => {
  let ativa: boolean;
  let router: Router;
  const urlLogin = (destino: string) => '/login?destino=' + encodeURIComponent(destino);

  beforeEach(() => {
    ativa = false;
    TestBed.configureTestingModule({ providers: [
      provideRouter([
        { path: 'login', component: LoginComponent },
        ...['', 'produtos', 'produtos/:id', 'carrinho', 'checkout', 'minha-conta', 'pedidos', 'pedidos/:id', 'cotacoes-correios/:id']
          .map(path => ({ path, component: LojaTeste })),
      ]),
      { provide: AuthService, useValue: {
        obterSessao: () => of(ativa ? { userId: id } : null),
        login: () => { ativa = true; return of(undefined); },
        logout: () => { ativa = false; return of(undefined); },
      } },
      { provide: CatalogoPublicoService, useValue: { listarCategorias: () => of([]) } },
    ] });
    router = TestBed.inject(Router);
  });

  it.each(destinos)('captura %s no cabeçalho desktop e mobile', async origem => {
    const harness = await RouterTestingHarness.create(origem);
    const view = harness.routeNativeElement!;
    for (const classe of ['.store-account__link', '.store-mobile-nav a']) {
      const entrar = [...view.querySelectorAll<HTMLAnchorElement>(classe)]
        .find(link => link.textContent.trim() === 'Entrar');
      expect(entrar).toBeDefined();
      const destino = router.parseUrl(entrar!.getAttribute('href')!);
      expect(destino.queryParamMap.get('destino')).toBe(origem);
    }
  });

  it('retorna ao carrinho após autenticar pelo formulário', async () => {
    const harness = await RouterTestingHarness.create(urlLogin('/carrinho'));
    const view = harness.routeNativeElement!;
    for (const [seletor, valor] of [['#email', 'cliente@example.test'], ['#senha', 'SenhaSegura!2026']]) {
      const input = view.querySelector<HTMLInputElement>(seletor)!;
      input.value = valor;
      input.dispatchEvent(new Event('input'));
    }
    view.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await vi.waitFor(() => expect(router.url).toBe('/carrinho'));
  });

  it.each([...destinos, '/checkout'])('sai de /login com sessão ativa para %s', async destino => {
    ativa = true;
    await RouterTestingHarness.create(urlLogin(destino));
    await vi.waitFor(() => expect(router.url).toBe(destino));
  });

  it('oferece atalhos e permite sair de uma sessão ativa sem retorno', async () => {
    ativa = true;
    const harness = await RouterTestingHarness.create('/login');
    const view = harness.routeNativeElement!;
    expect(view.querySelector('nav a[href="/"]')?.textContent).toContain('Voltar à loja');
    expect(view.querySelector('nav a[href="/pedidos"]')?.textContent).toContain('Meus pedidos');
    [...view.querySelectorAll('button')].find(b => b.textContent.trim() === 'Sair')!.click();
    harness.detectChanges();
    expect(ativa).toBe(false);
    expect(view.querySelector('form')).not.toBeNull();
    expect(router.url).toBe('/login');
  });

  it.each([
    'https://externo.example', '//externo.example', 'javascript:alert(1)', '/%2Fexterno.example',
    '/%61dmin', '/admin/pedidos', '/login', '/logout', '/nao-existe', '/produtos/identificador-invalido',
    '/pedidos/' + id + ';foo=1', '/(aux:checkout)', '/produtos?busca=%E0%A4',
  ])('rejeita %s e encaminha à home', async destino => {
    expect(new DefaultUrlSerializer().serialize(validarDestinoLoja(destino))).toBe('/');
    ativa = true;
    await RouterTestingHarness.create(urlLogin(destino));
    await vi.waitFor(() => expect(router.url).toBe('/'));
  });

  it('aceita links legados e elimina parâmetros sem função na rota', () => {
    const serializer = new DefaultUrlSerializer();
    expect(serializer.serialize(destinoLoginCliente(convertToParamMap({ retorno: 'checkout' }))!)).toBe('/checkout');
    expect(serializer.serialize(destinoLoginCliente(convertToParamMap({ pedido: id }))!)).toBe('/pedidos/' + id);
    expect(serializer.serialize(validarDestinoLoja('/produtos?busca=vela&returnUrl=//externo.example#catalogo')))
      .toBe('/produtos?busca=vela#catalogo');
  });
});
