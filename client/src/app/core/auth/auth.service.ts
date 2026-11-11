import { HttpClient, HttpErrorResponse, HttpHeaders, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, map, of, switchMap, throwError } from 'rxjs';
import { CsrfService } from '../security/csrf.service';

export interface AuthSession {
  userId: string;
}

interface LoginRequest {
  email: string;
  senha: string;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly csrf = inject(CsrfService);

  login(email: string, senha: string): Observable<void> {
    return this.postWithCsrf('/api/auth/login', { email, senha });
  }

  obterSessao(): Observable<AuthSession | null> {
    return this.http.get<AuthSession>('/api/auth/me').pipe(
      catchError((error: unknown) =>
        error instanceof HttpErrorResponse && error.status === 401
          ? of(null)
          : throwError(() => error),
      ),
    );
  }

  logout(): Observable<void> {
    return this.postWithCsrf('/api/auth/logout', null);
  }

  private postWithCsrf(url: string, body: LoginRequest | null): Observable<void> {
    return this.csrf.getRequestToken().pipe(
      switchMap(({ requestToken }) =>
        this.http.post<void>(url, body, {
          headers: new HttpHeaders({ 'X-XSRF-TOKEN': requestToken }),
          observe: 'response',
        }),
      ),
      map((response: HttpResponse<void>) => {
        if (response.status !== 204) {
          throw new Error('Unexpected authentication response');
        }
      }),
    );
  }
}
