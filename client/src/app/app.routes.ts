import { Routes } from '@angular/router';
import { CadastroComponent } from './cadastro/cadastro.component';

export const routes: Routes = [
  { path: 'cadastro', component: CadastroComponent },
  { path: '', pathMatch: 'full', redirectTo: 'cadastro' },
  { path: '**', redirectTo: 'cadastro' },
];
