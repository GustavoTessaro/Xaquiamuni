import { Routes } from '@angular/router';
import { CadastroComponent } from './cadastro/cadastro.component';
import { ConfirmEmailComponent } from './confirm-email/confirm-email.component';
import { ForgotPasswordComponent } from './forgot-password/forgot-password.component';
import { LoginComponent } from './login/login.component';

export const routes: Routes = [
  { path: 'confirm-email', component: ConfirmEmailComponent },
  { path: 'forgot-password', component: ForgotPasswordComponent },
  { path: 'login', component: LoginComponent },
  { path: 'cadastro', component: CadastroComponent },
  { path: '', pathMatch: 'full', redirectTo: 'cadastro' },
  { path: '**', redirectTo: 'cadastro' },
];
