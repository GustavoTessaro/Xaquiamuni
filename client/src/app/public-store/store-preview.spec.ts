import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { routes } from '../app.routes';
import { AdminAuthService } from '../core/admin-auth/admin-auth.service';
import { AuthService } from '../core/auth/auth.service';
import { TemasLojaService } from '../core/temas-loja.service';
import { CarrinhoService } from './carrinho.service';
import { CatalogoPublicoService } from './catalogo-publico.service';

describe('prévia administrativa da loja', () => {
  const previa = '/admin/temas/previa/original';
  const item = { id: 'produto-1', nome: 'Vela artesanal', categoriaNome: 'Velas', categoriaId: 'velas',
    fotoPrincipal: { id: 'foto', url: '/foto', opcoesIds: [] }, galeria: [],
    precoAPartirDe: 12, unidadesVenda: [], opcoes: [], medidas: [], disponivel: true };
  const detalhe = {
    ...item,
    fotoPrincipal: { ...item.fotoPrincipal, opcaoIds: [] },
    requerSelecaoDeOpcao: false,
    opcoes: [{ id: 'opcao-1', identificacao: 'Original', unidadeVenda: 'UN', precoVenda: 12,
      disponivel: true, pesoLiquidoGramas: null, volumeLiquidoMililitros: null, caracteristicas: [] }],
  };

  function configurar(admin = true) {
    const sessao = vi.fn(() => of(admin ? { userId: 'admin' } : null));
    const cliente = vi.fn(() => of({ userId: 'cliente' }));
    const carrinho = vi.fn(() => ({ atualizarDisponibilidade: vi.fn(), adicionar: vi.fn() }));
    const publicar = vi.fn();
    const consultarPublico = vi.fn(() => of({ chave: 'original', revisao: 0 }));
    const produto = vi.fn(() => of(detalhe));
    const catalogo = {
      listarCategorias: () => of([{ id: 'velas', nome: 'Velas', descricao: null }]),
      listarProdutos: () => of({ itens: [], pagina: 1, itensPorPagina: 12, totalPaginas: 0, totalItens: 0 }),
      consultarProduto: produto,
    };
    const rotas = routes.map(rota => rota.data?.['storePreview']
      ? { ...rota, providers: [{ provide: CatalogoPublicoService, useValue: catalogo }] }
      : rota);
    TestBed.configureTestingModule({
      providers: [
        provideRouter(rotas), provideHttpClient(),
        { provide: AdminAuthService, useValue: { getCurrentSession: sessao } },
        { provide: AuthService, useValue: { obterSessao: cliente } },
        { provide: CarrinhoService, useFactory: carrinho },
        { provide: TemasLojaService, useValue: {
          consultarPublico, publicar, consultarAdmin: () => of({
            publicacao: { chave: 'original', revisao: 0 }, temas: [],
          }),
        } },
      ],
    });
    return { cliente, carrinho, publicar, consultarPublico, sessao, produto };
  }

  it('reutiliza home, catálogo e detalhe sem ler sessão, carrinho ou tema publicado', async () => {
    const deps = configurar();
    const harness = await RouterTestingHarness.create(previa);
    const links = () => Array.from(harness.routeNativeElement!.querySelectorAll<HTMLAnchorElement>('a'),
      a => a.getAttribute('href'));
    expect(harness.routeNativeElement?.textContent).toContain('Prévia administrativa');
    expect(harness.routeNativeElement?.getAttribute('data-theme')).toBe('original');
    expect(links()).toContain(previa + '/produtos');
    expect(links()).toContain('/admin/temas');
    expect(links()).not.toContain('/carrinho');
    await harness.navigateByUrl(previa + '/produtos?categoriaId=velas');
    expect(harness.routeNativeElement?.querySelector('#catalog-title')).not.toBeNull();
    deps.produto.mockReturnValueOnce(throwError(() => new Error('Falha de catálogo')));
    await harness.navigateByUrl(previa + '/produtos/produto-1?busca=vela');
    expect(harness.routeNativeElement?.textContent).toContain('Tentar novamente');
    harness.routeNativeElement?.querySelector<HTMLButtonElement>('main button')?.click();
    harness.detectChanges();
    expect(harness.routeNativeElement?.textContent).toContain('Vela artesanal');
    expect(harness.routeNativeElement?.textContent).not.toContain('Adicionar ao carrinho');
    expect(links()).toContain(previa + '/produtos?busca=vela');
    expect(deps.sessao.mock.calls.length).toBeGreaterThanOrEqual(3);
    expect(deps.cliente).not.toHaveBeenCalled();
    expect(deps.carrinho).not.toHaveBeenCalled();
    expect(deps.publicar).not.toHaveBeenCalled();
    expect(deps.consultarPublico).not.toHaveBeenCalled();
  });

  it('recusa tema desconhecido, incompleto e rotas comerciais sob a prévia', async () => {
    configurar();
    const harness = await RouterTestingHarness.create();
    const router = TestBed.inject(Router);
    for (const invalida of ['/admin/temas/previa/natal', '/admin/temas/previa/desconhecido',
      previa + '/carrinho', previa + '/checkout', previa + '/pedidos/pedido-1']) {
      await harness.navigateByUrl(invalida);
      expect(router.url).toBe('/admin/temas');
    }
  });

  it('retorna ao login quando a sessão expira antes da próxima navegação', async () => {
    const deps = configurar();
    const harness = await RouterTestingHarness.create(previa);
    deps.sessao.mockReturnValue(of(null));
    await harness.navigateByUrl(previa + '/produtos/produto-1');
    expect(TestBed.inject(Router).url).toBe('/admin/login');
    expect(deps.produto).not.toHaveBeenCalled();
  });

  it('exige sessão ADMIN inclusive quando o acesso começa pelo detalhe', async () => {
    const deps = configurar(false);
    await RouterTestingHarness.create(previa + '/produtos/produto-1');
    expect(TestBed.inject(Router).url).toBe('/admin/login');
    expect(deps.produto).not.toHaveBeenCalled();
  });
});
