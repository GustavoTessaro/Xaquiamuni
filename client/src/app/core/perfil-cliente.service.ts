import { HttpClient, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map, switchMap, timeout } from 'rxjs';
import { CsrfService } from './security/csrf.service';
import type { EnderecoEntrega } from './atendimento.service';

export interface ResumoPerfilCliente {
  nomeCompleto: string | null;
  email: string | null;
  telefone: string | null;
  endereco: EnderecoEntrega | null;
}

export interface PerfilClienteVersionado extends ResumoPerfilCliente {
  versaoPerfil: string | null;
  versaoEndereco: string | null;
}

function lerPerfil(resposta: HttpResponse<ResumoPerfilCliente>): PerfilClienteVersionado {
  return {
    ...resposta.body!,
    versaoPerfil: resposta.headers.get('X-Perfil-Versao'),
    versaoEndereco: resposta.headers.get('X-Endereco-Versao'),
  };
}

@Injectable({ providedIn: 'root' })
export class PerfilClienteService {
  private readonly http = inject(HttpClient);

  private readonly csrf = inject(CsrfService);

  consultar() {
    return this.http.get<ResumoPerfilCliente>('/api/clientes/me/perfil', { observe: 'response' })
      .pipe(timeout(15_000), map(lerPerfil));
  }

  salvarPerfil(dados: { nomeCompleto: string; telefone: string }, versao: string) {
    return this.salvar('perfil', dados, versao);
  }

  salvarEndereco(dados: EnderecoEntrega, versao: string) {
    return this.salvar('endereco-padrao', dados, versao);
  }

  private salvar(recurso: string, dados: object, versao: string) {
    return this.csrf.getRequestToken().pipe(
      switchMap(({ requestToken }) => this.http.put<ResumoPerfilCliente>('/api/clientes/me/' + recurso, dados, {
        observe: 'response',
        headers: { 'X-XSRF-TOKEN': requestToken, 'If-Match': versao },
      })),
      timeout(15_000),
      map(lerPerfil),
    );
  }
}
