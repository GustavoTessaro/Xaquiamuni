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
  { path: '', pathMatch: 'full', redirectTo: 'cadastro' },
  { path: '**', redirectTo: 'cadastro' },
];
