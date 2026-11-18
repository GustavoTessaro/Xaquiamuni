import { Routes } from '@angular/router';
import { adminAuthGuard } from './admin/admin-auth.guard';
import { AdminDashboardComponent } from './admin/dashboard/admin-dashboard.component';
import { AdminCategoriasComponent } from './admin/categorias/admin-categorias.component';
import { AdminLoginComponent } from './admin/login/admin-login.component';
import { CadastroComponent } from './cadastro/cadastro.component';
import { ConfirmEmailComponent } from './confirm-email/confirm-email.component';
import { ForgotPasswordComponent } from './forgot-password/forgot-password.component';
import { LoginComponent } from './login/login.component';
import { ResetPasswordComponent } from './reset-password/reset-password.component';

export const routes: Routes = [
  { path: 'admin/login', component: AdminLoginComponent },
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
    ],
  },
  { path: '**', redirectTo: 'cadastro' },
];
