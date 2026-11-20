import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { switchMap } from 'rxjs';
import { CsrfService } from './security/csrf.service';

export interface DadosCompromisso {
  titulo: string;
  descricao: string | null;
  categoria: string | null;
  data: string;
  horarioInicial: string | null;
  horarioFinal: string | null;
}
export interface Compromisso extends DadosCompromisso {
  id: string;
  versao: string;
  criadoPorId: string;
  criadoEmUtc: string;
  atualizadoPorId: string;
  atualizadoEmUtc: string;
  excluidoPorId: string | null;
  excluidoEmUtc: string | null;
}

export const rotaAgenda = '/api/admin/agenda';
export type ComandoAgenda =
  | { operacao: 'criar'; metodo: 'POST'; url: string; corpo: DadosCompromisso & { versao: null } }
  | {
      operacao: 'editar';
      metodo: 'PUT';
      url: string;
      id: string;
      corpo: DadosCompromisso & { versao: string };
    }
  | { operacao: 'excluir'; metodo: 'DELETE'; url: string; id: string; versao: string; corpo: null };
export type TentativaAgenda = ComandoAgenda & { chave: string };
export interface ResultadoTentativaAgenda {
  operacao: ComandoAgenda['operacao'];
  recursoId: string;
  resultado: Compromisso;
}

@Injectable({ providedIn: 'root' })
export class AgendaService {
  private readonly http = inject(HttpClient);
  private readonly csrf = inject(CsrfService);

  listar(dataInicial: string, dataFinal: string) {
    return this.http.get<Compromisso[]>(rotaAgenda, { params: { dataInicial, dataFinal } });
  }

  obter(id: string) {
    return this.http.get<Compromisso>(rotaAgenda + '/' + encodeURIComponent(id));
  }

  tentativa(chave: string) {
    return this.http.get<ResultadoTentativaAgenda>(
      rotaAgenda + '/tentativas/' + encodeURIComponent(chave),
    );
  }

  enviar(tentativa: TentativaAgenda) {
    return this.csrf.getRequestToken().pipe(
      switchMap(({ requestToken }) =>
        this.http.request<Compromisso>(tentativa.metodo, tentativa.url, {
          body: tentativa.corpo,
          headers: {
            'X-XSRF-TOKEN': requestToken,
            'Idempotency-Key': tentativa.chave,
          },
        }),
      ),
    );
  }
}
