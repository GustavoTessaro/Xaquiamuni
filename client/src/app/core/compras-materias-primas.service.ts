import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { switchMap } from 'rxjs';
import { UnidadeBaseEstoque } from './admin-estoque/insumos.service';
import { CsrfService } from './security/csrf.service';

export interface ResumoCompra {
  id: string;
  fornecedorId: string;
  nomeFornecedor: string;
  dataCompra: string;
  situacao: 'Rascunho' | 'Recebida' | 'Cancelada';
  total: string | null;
}

export interface ComponenteCompraInput {
  insumoId: string;
  apresentacaoId: string | null;
  quantidade: string;
  snapshotId: string | null;
}

export interface CompraInput {
  fornecedorId: string;
  dataCompra: string;
  versao: string | null;
  linhas: Array<{
    kit: string | null;
    quantidade: string;
    valorTotal: string | null;
    componentes: ComponenteCompraInput[];
  }>;
}

export interface DetalheCompra {
  resumo: ResumoCompra;
  versao: string;
  recebidaEmUtc?: string | null;
  recebidaPorId?: string | null;
  linhas: Array<{
    kit: string | null;
    quantidade: string;
    valorTotal: string | null;
    componentes: Array<
      Omit<ComponenteCompraInput, 'snapshotId'> & {
        id: string;
        nomeInsumo: string;
        nomeApresentacao: string | null;
        fator: string | null;
        quantidadeBase: string;
        unidadeBase: UnidadeBaseEstoque;
      }
    >;
  }>;
}

export interface ReceberCompraInput {
  versao: string;
  validades: Array<{ componenteId: string; dataValidade: string }>;
}

export interface RecebimentoCompra {
  compraId: string;
  situacao: 'Recebida';
  versao: string;
  recebidaEmUtc: string;
  recebidaPorId: string;
  entradas: Array<{ linhaId: string; componenteId: string; movimentoId: string }>;
}

@Injectable({ providedIn: 'root' })
export class ComprasMateriasPrimasService {
  private readonly http = inject(HttpClient);
  private readonly csrf = inject(CsrfService);
  private readonly rota = '/api/admin/compras-materias-primas';

  listar() {
    return this.http.get<ResumoCompra[]>(this.rota);
  }

  obter(id: string) {
    return this.http.get<DetalheCompra>(`${this.rota}/${encodeURIComponent(id)}`);
  }

  consultarCriacao(chave: string) {
    return this.http.get<DetalheCompra>(`${this.rota}/tentativas/${encodeURIComponent(chave)}`);
  }

  salvar(id: string | null, compra: CompraInput, chave: string) {
    return this.csrf.getRequestToken().pipe(
      switchMap(({ requestToken }) => {
        const headers = { 'X-XSRF-TOKEN': requestToken };
        return id
          ? this.http.put<DetalheCompra>(`${this.rota}/${encodeURIComponent(id)}`, compra, {
              headers,
            })
          : this.http.post<DetalheCompra>(this.rota, compra, {
              headers: { ...headers, 'Idempotency-Key': chave },
            });
      }),
    );
  }

  receber(id: string, corpo: ReceberCompraInput, chave: string) {
    return this.csrf
      .getRequestToken()
      .pipe(
        switchMap(({ requestToken }) =>
          this.http.post<RecebimentoCompra>(
            `${this.rota}/${encodeURIComponent(id)}/receber`,
            corpo,
            { headers: { 'X-XSRF-TOKEN': requestToken, 'Idempotency-Key': chave } },
          ),
        ),
      );
  }

  cancelar(id: string, versao: string) {
    return this.csrf
      .getRequestToken()
      .pipe(
        switchMap(({ requestToken }) =>
          this.http.post<DetalheCompra>(
            `${this.rota}/${encodeURIComponent(id)}/cancelar`,
            { versao },
            { headers: { 'X-XSRF-TOKEN': requestToken } },
          ),
        ),
      );
  }
}
