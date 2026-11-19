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

export interface ConsumoProducao {
  insumoId: string;
  unidadeBase: Material['unidade'];
  quantidade: string;
}

export interface ConcluirProducao {
  versao: string;
  quantidadeProduzida: number;
  consumos: ConsumoProducao[];
  dataFabricacao: string | null;
  dataValidade: string | null;
}

export interface ConclusaoProducao {
  ordemId: string;
  situacao: 'Concluida';
  versao: string;
  quantidadeProduzida: number;
  loteId: string;
  codigoLote: string;
  movimentoEntradaId: string;
  dataFabricacao: string;
  dataValidade: string;
  responsavelId: string;
  concluidaEmUtc: string;
  consumos: {
    insumoId: string;
    nome: string;
    unidadeBase: Material['unidade'];
    consumida: string;
    liberada: string;
  }[];
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
  versaoOrdem: string;
  situacao: 'EmAndamento' | 'Cancelada' | 'Concluida';
  iniciadaEmUtc: string;
  encerradaEmUtc: string | null;
}

export interface DetalheOrdem {
  resumo: ResumoOrdem;
  materiais: (Material & { reservaInelegivel: boolean })[];
  varianteId: string;
  conclusao: ConclusaoProducao | null;
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

  concluir(id: string, corpo: ConcluirProducao, chave: string) {
    return this.post<ConclusaoProducao>(
      `${this.rota}/${encodeURIComponent(id)}/concluir`,
      corpo,
      chave,
    );
  }

  private post<T = { ordemId: string }>(url: string, corpo: object, chave: string) {
    return this.csrf.getRequestToken().pipe(
      switchMap(({ requestToken }) =>
        this.http.post<T>(url, corpo, {
          headers: { 'X-XSRF-TOKEN': requestToken, 'Idempotency-Key': chave },
        }),
      ),
    );
  }
}
