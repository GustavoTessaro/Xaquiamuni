import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, ElementRef, HostListener, input, inject, signal, viewChild, ViewEncapsulation } from '@angular/core';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { filter, finalize } from 'rxjs';
import { toSignal } from '@angular/core/rxjs-interop';
import { AdminAuthService } from '../core/admin-auth/admin-auth.service';

export type AdminWorkspacePage =
  'overview' | 'categories' | 'products' | 'stock' | 'orders' | 'pix' | 'suppliers' | 'purchases' | 'recipes' | 'production' | 'external-sales' | 'finance' | 'agenda' | 'themes' | 'promotions';

interface GrupoAdmin {
  titulo: string;
  icone?: string;
  itens: readonly { pagina: AdminWorkspacePage; nome: string; rota: string }[];
}

const grupos: readonly GrupoAdmin[] = [
  { titulo: '', itens: [{ pagina: 'overview', nome: 'Vis\u00e3o geral', rota: '/admin' }] },
  { titulo: 'Vendas', icone: 'M4 5h2l2 10h11l2-7H7M9 19h.01M18 19h.01', itens: [
    { pagina: 'orders', nome: 'Pedidos', rota: '/admin/pedidos' },
    { pagina: 'pix', nome: 'PIX manual', rota: '/admin/configuracoes/pix-manual' },
    { pagina: 'external-sales', nome: 'Vendas externas', rota: '/admin/vendas-externas' },
  ] },
  { titulo: 'Cat\u00e1logo', icone: 'M4 4h6v6H4zM14 4h6v6h-6zM4 14h6v6H4zM14 14h6v6h-6z', itens: [
    { pagina: 'products', nome: 'Produtos', rota: '/admin/produtos' },
    { pagina: 'categories', nome: 'Categorias', rota: '/admin/categorias' },
    { pagina: 'promotions', nome: 'Promoções', rota: '/admin/promocoes' },
  ] },
  { titulo: 'Produ\u00e7\u00e3o e estoque', icone: 'M12 3l8 4v10l-8 4-8-4V7zM4 7l8 4 8-4M12 11v10', itens: [
    { pagina: 'recipes', nome: 'Receitas', rota: '/admin/receitas' },
    { pagina: 'production', nome: 'Produ\u00e7\u00e3o', rota: '/admin/producao' },
    { pagina: 'stock', nome: 'Mat\u00e9rias-primas', rota: '/admin/estoque/insumos' },
  ] },
  { titulo: 'Gest\u00e3o', icone: 'M4 7h16v13H4zM8 7V4h8v3M4 12h16', itens: [
    { pagina: 'suppliers', nome: 'Fornecedores', rota: '/admin/fornecedores' },
    { pagina: 'purchases', nome: 'Compras', rota: '/admin/compras' },
  ] },
  { titulo: 'Organiza\u00e7\u00e3o', icone: 'M4 6h16v14H4zM8 4v4m8-4v4M4 10h16m-12 5h3', itens: [
    { pagina: 'agenda', nome: 'Agenda', rota: '/admin/agenda' },
    { pagina: 'finance', nome: 'Financeiro', rota: '/admin/financeiro' },
  ] },
  { titulo: 'Apar\u00eancia', icone: 'M4 5h16v14H4zM4 9h16m-10 0v10', itens: [
    { pagina: 'themes', nome: 'Temas da loja', rota: '/admin/temas' },
  ] },
];

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgTemplateOutlet, RouterLink],
  encapsulation: ViewEncapsulation.None,
  selector: 'app-admin-workspace',
  styleUrl: './admin-workspace.component.css',
  templateUrl: './admin-workspace.component.html',
})
export class AdminWorkspaceComponent {
  readonly activePage = input.required<AdminWorkspacePage>();
  protected readonly grupos = grupos;
  protected readonly menuAberto = signal(false);
  private readonly menu = viewChild.required<ElementRef<HTMLDialogElement>>('menu');
  private readonly menuClose = viewChild.required<ElementRef<HTMLButtonElement>>('menuClose');
  private readonly menuTrigger = viewChild.required<ElementRef<HTMLButtonElement>>('menuTrigger');
  protected readonly signingOut = signal(false);
  protected readonly errorMessage = signal('');

  private readonly adminAuthService = inject(AdminAuthService);
  private readonly router = inject(Router);

  private readonly navegacao = toSignal(this.router.events.pipe(filter(event => event instanceof NavigationEnd)));
  protected readonly paginaAtual = computed(() => {
    const pagina = this.activePage();
    if (pagina !== 'stock') return pagina;
    this.navegacao();
    return this.router.isActive('/admin/estoque/insumos', {
      paths: 'subset', queryParams: 'ignored', fragment: 'ignored', matrixParams: 'ignored',
    }) ? pagina : null;
  });

  protected abrirMenu(): void {
    const menu = this.menu().nativeElement;
    if (menu.open) return;

    this.menuAberto.set(true);
    menu.showModal();
    this.menuClose().nativeElement.focus();
  }

  protected fecharMenu(): void {
    this.menu().nativeElement.close();
  }

  protected menuFechado(): void {
    this.menuAberto.set(false);
    this.menuTrigger().nativeElement.focus();
  }

  @HostListener('window:resize')
  protected fecharMenuAoExpandir(): void {
    if (this.menuAberto() && this.menuTrigger().nativeElement.getClientRects().length === 0) {
      this.fecharMenu();
    }
  }

  protected logout(): void {
    if (this.signingOut()) {
      return;
    }

    this.signingOut.set(true);
    this.errorMessage.set('');
    this.adminAuthService
      .logout()
      .pipe(finalize(() => this.signingOut.set(false)))
      .subscribe({
        next: () => {
          void this.router.navigateByUrl('/admin/login');
        },
        error: () => {
          this.errorMessage.set('Não foi possível encerrar a sessão. Tente novamente.');
        },
      });
  }
}
