import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { switchMap } from 'rxjs';
import { CsrfService } from './security/csrf.service';

export interface InicioProducao {
  receitaId: string;
  receitaVersaoId: string;
  quantidade: number;
}

interface Material {
  insumoId: string;
  nome: string;
  unidade: 'Grama' | 'Mililitro' | 'Unidade';
  necessario: string;
  reservado: string;
}

export interface PreviaProducao {
  receitaVersaoId: string;
  versao: number;
  materiais: (Material & { utilizavel: string; livre: string; falta: string })[];
}

export interface ResumoOrdem {
  id: string;
  produtoId: string;
  produto: string;
  variante: string;
  receitaId: string;
  receita: string;
  versao: number;
  quantidade: number;
  situacao: 'EmAndamento' | 'Cancelada' | 'Concluida';
  iniciadaEmUtc: string;
  encerradaEmUtc: string | null;
}

export interface DetalheOrdem {
  resumo: ResumoOrdem;
  materiais: (Material & { reservaInelegivel: boolean })[];
}

@Injectable({ providedIn: 'root' })
export class OrdensProducaoService {
  private readonly http = inject(HttpClient);
  private readonly csrf = inject(CsrfService);
  private readonly rota = '/api/admin/producao';

  listar() {
    return this.http.get<ResumoOrdem[]>(this.rota);
  }

  obter(id: string) {
    return this.http.get<DetalheOrdem>(`${this.rota}/${encodeURIComponent(id)}`);
  }

  tentativa(chave: string) {
    return this.http.get<{ ordemId: string }>(
      `${this.rota}/tentativas/${encodeURIComponent(chave)}`,
    );
  }

  prever(receitaId: string, quantidade: number) {
    return this.http.get<PreviaProducao>(`${this.rota}/previa`, {
      params: { receitaId, quantidade },
    });
  }

  iniciar(corpo: InicioProducao, chave: string) {
    return this.post(this.rota, corpo, chave);
  }

  cancelar(id: string, chave: string) {
    return this.post(`${this.rota}/${encodeURIComponent(id)}/cancelar`, {}, chave);
  }

  private post(url: string, corpo: object, chave: string) {
    return this.csrf
      .getRequestToken()
      .pipe(
        switchMap(({ requestToken }) =>
          this.http.post<{ ordemId: string }>(url, corpo, {
            headers: { 'X-XSRF-TOKEN': requestToken, 'Idempotency-Key': chave },
          }),
        ),
      );
  }
}
