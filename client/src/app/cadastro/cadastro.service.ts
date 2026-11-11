import { HttpClient, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map, switchMap } from 'rxjs';
import { CsrfService } from '../core/security/csrf.service';

export interface RegisterClientAddressRequest {
  cep: string;
  uf: string;
  cidade: string;
  bairro: string;
  logradouro: string;
  numero: string;
  complemento: string | null;
}

export interface RegisterClientRequest {
  nomeCompleto: string;
  cpf: string;
  email: string;
  telefone: string;
  senha: string;
  enderecoInicial: RegisterClientAddressRequest;
}

export interface RegisterClientResponse {
  userId: string;
}

@Injectable({ providedIn: 'root' })
export class CadastroService {
  private readonly http = inject(HttpClient);
  private readonly csrf = inject(CsrfService);

  register(request: RegisterClientRequest): Observable<RegisterClientResponse> {
    return this.csrf.getRequestToken().pipe(
      switchMap(() =>
        this.http.post<RegisterClientResponse>('/api/auth/register', request, {
          observe: 'response',
        }),
      ),
      map((response: HttpResponse<RegisterClientResponse>) => {
        if (response.status !== 201 || !response.body?.userId) {
          throw new Error('Unexpected registration response');
        }
        return response.body;
      }),
    );
  }
}
