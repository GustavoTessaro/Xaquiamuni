import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { switchMap } from 'rxjs';
import { CsrfService } from './security/csrf.service';

export interface TemaPublicado {
  chave: string;
  revisao: number;
}

export interface PublicacaoTemaAdmin extends TemaPublicado {
  publicadoEmUtc: string | null;
  administradorId: string | null;
}

export interface TemaLojaAdmin {
  chave: string;
  nome: string;
  descricao: string;
  disponivel: boolean;
}

export interface CatalogoTemasAdmin {
  publicacao: PublicacaoTemaAdmin;
  temas: TemaLojaAdmin[];
}

@Injectable({ providedIn: 'root' })
export class TemasLojaService {
  private readonly http = inject(HttpClient);
  private readonly csrf = inject(CsrfService);
  private readonly adminUrl = '/api/admin/loja/tema';

  consultarPublico() {
    return this.http.get<TemaPublicado>('/api/loja/tema');
  }

  consultarAdmin() {
    return this.http.get<CatalogoTemasAdmin>(this.adminUrl);
  }

  publicar(chave: string, revisaoEsperada: number, idempotencia: string) {
    return this.csrf.getRequestToken().pipe(
      switchMap(({ requestToken }) => this.http.put<PublicacaoTemaAdmin>(
        this.adminUrl,
        { chave, revisaoEsperada },
        { headers: { 'X-XSRF-TOKEN': requestToken, 'Idempotency-Key': idempotencia } },
      )),
    );
  }
}
