import { inject } from '@angular/core';
import { Router, Routes } from '@angular/router';
import { adminAuthGuard } from './admin/admin-auth.guard';
import { AdminDashboardComponent } from './admin/dashboard/admin-dashboard.component';
import { AdminCategoriasComponent } from './admin/categorias/admin-categorias.component';
import { AdminLoginComponent } from './admin/login/admin-login.component';
import { CadastroComponent } from './cadastro/cadastro.component';
import { ConfirmEmailComponent } from './confirm-email/confirm-email.component';
import { ForgotPasswordComponent } from './forgot-password/forgot-password.component';
import { LoginComponent } from './login/login.component';
import { ResetPasswordComponent } from './reset-password/reset-password.component';
import { CatalogoPublicoService } from './public-store/catalogo-publico.service';
import { buscarManifestoTemaLoja } from './public-store/manifestos-tema-loja';

const paginasVitrine: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./public-store/store-home.component').then((module) => module.StoreHomeComponent),
    pathMatch: 'full',
  },
  {
    path: 'produtos/:id',
    loadComponent: () =>
      import('./public-store/produto-detalhe.component').then(
        (module) => module.ProdutoDetalheComponent,
      ),
  },
  {
    path: 'produtos',
    loadComponent: () =>
      import('./public-store/catalogo-publico.component').then(
        (module) => module.CatalogoPublicoComponent,
      ),
  },
];

export const routes: Routes = [
  { path: 'admin/login', component: AdminLoginComponent },
  {
    path: 'admin/configuracoes/pix-manual',
    loadComponent: () =>
      import('./admin/pix/admin-pix.component').then((module) => module.AdminPixComponent),
    canActivate: [adminAuthGuard],
  },
  {
    path: 'admin/pedidos/:pedidoId',
    loadComponent: () =>
      import('./admin/pedidos/admin-pedido-detalhe.component').then(
        (module) => module.AdminPedidoDetalheComponent,
      ),
    canActivate: [adminAuthGuard],
  },
  {
    path: 'admin/pedidos',
    loadComponent: () =>
      import('./admin/pedidos/admin-pedidos.component').then(
        (module) => module.AdminPedidosComponent,
      ),
    canActivate: [adminAuthGuard],
  },
  {
    path: 'admin/estoque/insumos/operacoes',
    loadComponent: () =>
      import('./admin/estoque/operacoes/admin-operacao-insumo.component').then(
        (module) => module.AdminOperacaoInsumoComponent,
      ),
    canActivate: [adminAuthGuard],
  },
  {
    path: 'admin/estoque/insumos/entradas',
    loadComponent: () =>
      import('./admin/estoque/entradas/admin-entrada-insumo.component').then(
        (module) => module.AdminEntradaInsumoComponent,
      ),
    canActivate: [adminAuthGuard],
  },
  {
    path: 'admin/estoque/insumos/:id/movimentacoes/:movimentoId/estornar',
    loadComponent: () =>
      import('./admin/estoque/operacoes/admin-operacao-insumo.component').then(
        (module) => module.AdminOperacaoInsumoComponent,
      ),
    canActivate: [adminAuthGuard],
  },
  {
    path: 'admin/estoque/insumos/novo',
    loadComponent: () =>
      import('./admin/estoque/insumos/admin-insumo-form.component').then(
        (module) => module.AdminInsumoFormComponent,
      ),
    canActivate: [adminAuthGuard],
  },
  {
    path: 'admin/estoque/insumos/:id/consultas',
    loadComponent: () =>
      import('./admin/estoque/consultas/admin-consulta-insumo.component').then(
        (module) => module.AdminConsultaInsumoComponent,
      ),
    canActivate: [adminAuthGuard],
  },
  {
    path: 'admin/estoque/insumos/:id',
    loadComponent: () =>
      import('./admin/estoque/insumos/admin-insumo-form.component').then(
        (module) => module.AdminInsumoFormComponent,
      ),
    canActivate: [adminAuthGuard],
  },
  {
    path: 'admin/estoque/insumos',
    loadComponent: () =>
      import('./admin/estoque/insumos/admin-insumos.component').then(
        (module) => module.AdminInsumosComponent,
      ),
    canActivate: [adminAuthGuard],
  },
  {
    path: 'admin/estoque/produtos-acabados/:varianteId',
    loadComponent: () =>
      import('./admin/estoque/produtos-acabados/admin-produtos-acabados.component').then(
        (module) => module.AdminProdutosAcabadosComponent,
      ),
    canActivate: [adminAuthGuard],
  },
  {
    path: 'admin/estoque/produtos-acabados',
    loadComponent: () =>
      import('./admin/estoque/produtos-acabados/admin-produtos-acabados.component').then(
        (module) => module.AdminProdutosAcabadosComponent,
      ),
    canActivate: [adminAuthGuard],
  },
  {
    path: 'admin/produtos/:produtoId/estoque',
    loadComponent: () =>
      import('./admin/estoque/produtos-acabados/admin-produtos-acabados.component').then(
        (module) => module.AdminProdutosAcabadosComponent,
      ),
    canActivate: [adminAuthGuard],
  },
  {
    path: 'admin/produtos/:produtoId/fotos',
    loadComponent: () =>
      import('./admin/produtos/admin-produto-imagens.component').then(
        (module) => module.AdminProdutoImagensComponent,
      ),
    canActivate: [adminAuthGuard],
  },
  {
    path: 'admin/produtos',
    loadComponent: () =>
      import('./admin/produtos/admin-produtos.component').then(
        (module) => module.AdminProdutosComponent,
      ),
    canActivate: [adminAuthGuard],
  },
  {
    path: 'admin/producao',
    loadComponent: () => import('./admin/producao/admin-producao.component').then(m => m.AdminProducaoComponent),
    canActivate: [adminAuthGuard],
  },
  {
    path: 'admin/receitas',
    loadComponent: () => import('./admin/receitas/admin-receitas.component').then(m => m.AdminReceitasComponent),
    canActivate: [adminAuthGuard],
  },
  {
    path: 'admin/vendas-externas/:id',
    loadComponent: () => import('./admin/vendas-externas/admin-vendas-externas.component').then(m => m.AdminVendasExternasComponent),
    canActivate: [adminAuthGuard],
  },
  {
    path: 'admin/vendas-externas',
    loadComponent: () => import('./admin/vendas-externas/admin-vendas-externas.component').then(m => m.AdminVendasExternasComponent),
    canActivate: [adminAuthGuard],
  },
  {
    path: 'admin/temas/previa/:tema',
    loadComponent: () => import('./public-store/store-layout.component').then(m => m.StoreLayoutComponent),
    data: { storePreview: true },
    providers: [CatalogoPublicoService],
    canActivate: [route => buscarManifestoTemaLoja(route.paramMap.get('tema'))
      ? true : inject(Router).createUrlTree(['/admin/temas'])],
    canActivateChild: [adminAuthGuard],
    children: [
      ...paginasVitrine.map(page => ({ ...page, runGuardsAndResolvers: 'always' as const })),
      { path: '**', redirectTo: '/admin/temas' },
    ],
  },
  {
    path: 'admin/temas',
    loadComponent: () => import('./admin/temas/admin-temas.component').then(m => m.AdminTemasComponent),
    canActivate: [adminAuthGuard],
  },
  {
    path: 'admin/agenda',
    loadComponent: () =>
      import('./admin/agenda/admin-agenda.component').then((module) => module.AdminAgendaComponent),
    canActivate: [adminAuthGuard],
  },
  {
    path: 'admin/financeiro',
    loadComponent: () =>
      import('./admin/financeiro/admin-financeiro.component').then(
        (module) => module.AdminFinanceiroComponent,
      ),
    canActivate: [adminAuthGuard],
  },
  {
    path: 'admin/compras/apresentacoes/:id',
    loadComponent: () => import('./admin/estoque/insumos/admin-insumo-form.component').then(
      (module) => module.AdminInsumoFormComponent,
    ),
    data: { apresentacoes: true },
    canActivate: [adminAuthGuard],
  },
  {
    path: 'admin/compras',
    loadComponent: () => import('./admin/compras/admin-compras-materias-primas.component').then(
      (module) => module.AdminComprasMateriasPrimasComponent,
    ),
    canActivate: [adminAuthGuard],
  },
  {
    path: 'admin/fornecedores',
    loadComponent: () => import('./admin/fornecedores/admin-fornecedores.component').then((module) => module.AdminFornecedoresComponent),
    canActivate: [adminAuthGuard],
  },
  { path: 'admin/categorias', component: AdminCategoriasComponent, canActivate: [adminAuthGuard] },
  { path: 'admin', component: AdminDashboardComponent, canActivate: [adminAuthGuard] },
  { path: 'confirm-email', component: ConfirmEmailComponent },
  { path: 'forgot-password', component: ForgotPasswordComponent },
  { path: 'login', component: LoginComponent },
  { path: 'reset-password', component: ResetPasswordComponent },
  { path: 'cadastro', component: CadastroComponent },
  {
    path: '',
    loadComponent: () =>
      import('./public-store/store-layout.component').then((module) => module.StoreLayoutComponent),
    children: [
      {
        path: 'checkout',
        loadComponent: () =>
          import('./public-store/checkout.component').then((module) => module.CheckoutComponent),
      },
      {
        path: 'pedidos',
        loadComponent: () =>
          import('./public-store/meus-pedidos.component').then(
            (module) => module.MeusPedidosComponent,
          ),
      },
      {
        path: 'pedidos/:id',
        loadComponent: () =>
          import('./public-store/pedido-confirmacao.component').then(
            (module) => module.PedidoConfirmacaoComponent,
          ),
      },
      {
        path: 'carrinho',
        loadComponent: () =>
          import('./public-store/carrinho.component').then((module) => module.CarrinhoComponent),
      },
      ...paginasVitrine,
    ],
  },
  { path: '**', redirectTo: 'cadastro' },
];
