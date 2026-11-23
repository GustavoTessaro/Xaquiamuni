import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { AdminAuthService } from '../../core/admin-auth/admin-auth.service';
import { AdminDashboardComponent } from './admin-dashboard.component';

describe('AdminDashboardComponent', () => {
  let http: HttpTestingController;
  let fixture: ComponentFixture<AdminDashboardComponent>;
  let logout: ReturnType<typeof vi.fn<AdminAuthService['logout']>>;
  let navigateByUrl: ReturnType<typeof vi.fn<Router['navigateByUrl']>>;

  beforeEach(async () => {
    logout = vi.fn<AdminAuthService['logout']>();
    logout.mockReturnValue(of(undefined));
    await TestBed.configureTestingModule({
      imports: [AdminDashboardComponent],
      providers: [
        provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
        {
          provide: AdminAuthService,
          useValue: {
            login: vi.fn(),
            getCurrentSession: vi.fn(),
            logout,
          },
        },
      ],
    }).compileComponents();

    navigateByUrl = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(AdminDashboardComponent);
    fixture.detectChanges();
  });

  afterEach(() => {
    http.match(() => true).forEach(req => req.flush(null, { status: 503, statusText: "Offline" }));
    http.verify();
    vi.restoreAllMocks();
  });

  it('shows the initial panel and links to available product and category management', () => {
    const content = fixture.nativeElement.textContent;

    expect(content).toContain('Visão geral');
    expect(content).toContain('Produtos');
    expect(content).toContain('Categorias');
    expect(fixture.nativeElement.querySelector('a[href="/admin"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('a[href="/admin/produtos"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('a[href="/admin/produtos"]').textContent).toContain('Produtos');
    expect(fixture.nativeElement.querySelector('a[href="/admin/categorias"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('a[href="/admin/categorias"]').textContent).toContain('Categorias');
  });

  it('exibe dados reais e diferencia falha de uma contagem zerada', () => {
    http.expectOne('/api/admin/dashboard/operacao').flush({
      pixNoPrazo: 4, pixVencido: 1, aPreparar: 2, emPreparacao: 3,
      prontosParaRetirada: 0, producoesEmAndamento: 1, comprasRascunho: 2,
    });
    http.expectOne('/api/admin/dashboard/estoque').flush(null, { status: 503, statusText: 'Offline' });
    http.expectOne('/api/admin/dashboard/agenda').flush([
      { data: '2026-11-20', horarioInicial: null, titulo: 'Planejamento' },
      { data: '2026-11-21', horarioInicial: '09:30:00', titulo: 'Reuni\u00e3o' },
    ]);
    http.expectOne('/api/admin/financeiro/resumo').flush({
      fluxoPeriodo: { dataInicial: null, dataFinal: null, entradasRecebidasCentavos: '1234',
        saidasPagasCentavos: '1500', movimentacaoLiquidaCentavos: '-266' },
      posicaoAtual: { aReceberCentavos: '700', aPagarCentavos: '900', comprasValorAConferir: 2 },
    });
    fixture.detectChanges();
    const tela = fixture.nativeElement as HTMLElement;
    expect(tela.querySelector('meter')?.max).toBe(10);
    expect(tela.querySelector('meter')?.value).toBe(4);
    expect(tela.querySelector('a[href="/admin/pedidos?situacao=AguardandoPagamento"]')).not.toBeNull();
    expect(tela.textContent).not.toContain('Produtos dispon\u00edveis para venda');
    expect(tela.textContent).toContain('indicadores de estoque');
    expect(tela.textContent).toContain('Dia inteiro');
    expect(tela.textContent).toContain('09:30');
    expect(tela.textContent).toContain('2,66');

    (tela.querySelector('.dashboard__heading button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(http.match(req => req.url.startsWith('/api/admin/dashboard')).length).toBe(3);
    http.expectOne('/api/admin/financeiro/resumo').flush(null, { status: 503, statusText: 'Offline' });
    fixture.detectChanges();
    expect(tela.querySelector('.dashboard__panel [role=status]')).not.toBeNull();
  });

  it('groups modules and marks the overview as the current page', () => {
    const nav = fixture.nativeElement.querySelector('.admin-sidebar nav') as HTMLElement;
    expect(nav.querySelectorAll('a')).toHaveLength(16);
    expect(nav.querySelector('a[href="/admin/configuracoes/atendimento"]')).not.toBeNull();
    expect(nav.querySelector('a[href="/admin/promocoes"]')?.textContent).toContain('Promoções');
    expect(nav.querySelectorAll('[aria-current="page"]')).toHaveLength(1);
    expect(nav.querySelector('[aria-current="page"]')?.getAttribute('href')).toBe('/admin');
    expect(nav.querySelectorAll('.admin-nav__label')).toHaveLength(6);
  });

  it('logs out and returns to the administrative login', () => {
    const button = fixture.nativeElement.querySelector('.admin-signout') as HTMLButtonElement;
    button.click();
    fixture.detectChanges();

    expect(logout).toHaveBeenCalledOnce();
    expect(navigateByUrl).toHaveBeenCalledWith('/admin/login');
  });

  it('keeps the panel visible and reports a logout failure', () => {
    logout.mockReturnValue(throwError(() => new Error('network failure')));
    const button = fixture.nativeElement.querySelector('.admin-signout') as HTMLButtonElement;
    button.click();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Não foi possível encerrar a sessão.');
    expect(navigateByUrl).not.toHaveBeenCalled();
  });
});
