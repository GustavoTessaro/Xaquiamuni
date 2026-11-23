import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { timeout } from 'rxjs';
import type { EnderecoEntrega } from './atendimento.service';

export interface ResumoPerfilCliente {
  nomeCompleto: string | null;
  email: string | null;
  telefone: string | null;
  endereco: EnderecoEntrega | null;
}

@Injectable({ providedIn: 'root' })
export class PerfilClienteService {
  private readonly http = inject(HttpClient);

  consultar() {
    return this.http.get<ResumoPerfilCliente>('/api/clientes/me/perfil').pipe(timeout(15_000));
  }
}
