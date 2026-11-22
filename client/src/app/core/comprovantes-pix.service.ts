import { DOCUMENT } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { catchError, of, switchMap, throwError, timeout } from 'rxjs';
import { CsrfService } from './security/csrf.service';

export interface ComprovantePix {
  id: string;
  pedidoId: string;
  versao: string;
  enviadoEmUtc: string;
  tipo: string;
  tamanhoBytes: number;
}

export interface TentativaComprovantePix {
  pedidoId: string;
  chave: string;
  arquivo: File;
  versao: string | null;
}

@Injectable({ providedIn: 'root' })
export class ComprovantesPixService {
  private readonly http = inject(HttpClient);
  private readonly csrf = inject(CsrfService);
  private readonly documento = inject(DOCUMENT);

  consultar(id: string, admin: boolean) {
    return this.http.get<ComprovantePix>(this.url(id, admin)).pipe(
      timeout(30_000),
      catchError((erro: unknown) =>
        erro instanceof HttpErrorResponse && erro.status === 404 ? of(null) : throwError(() => erro),
      ),
    );
  }

  enviar(tentativa: TentativaComprovantePix) {
    const dados = new FormData();
    dados.append('arquivo', tentativa.arquivo);
    return this.csrf.getRequestToken().pipe(
      switchMap(({ requestToken }) => this.http.post<ComprovantePix>(this.url(tentativa.pedidoId, false), dados, {
        headers: {
          'X-XSRF-TOKEN': requestToken,
          'Idempotency-Key': tentativa.chave,
          ...(tentativa.versao ? { 'X-Comprovante-Versao': tentativa.versao } : {}),
        },
      })),
      timeout(60_000),
    );
  }

  baixar(id: string, admin: boolean) {
    return this.http.get(this.url(id, admin) + '/conteudo', { responseType: 'blob' }).pipe(timeout(30_000));
  }

  salvarDownload(blob: Blob): void {
    const urlApi = this.documento.defaultView?.URL;
    if (!urlApi?.createObjectURL) throw new Error('Download indisponível neste navegador.');
    const url = urlApi.createObjectURL(blob);
    const link = this.documento.createElement('a');
    link.href = url;
    const extensao = blob.type === 'application/pdf' ? 'pdf' : blob.type === 'image/png' ? 'png' : 'jpg';
    link.download = 'comprovante-pix.' + extensao;
    try {
      this.documento.body.append(link);
      link.click();
    } finally {
      link.remove();
      setTimeout(() => urlApi.revokeObjectURL(url), 1000);
    }
  }

  private url(id: string, admin: boolean): string {
    return `/api/${admin ? 'admin/' : ''}pedidos/${encodeURIComponent(id)}/comprovante-pix`;
  }
}
