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

interface EmailConfirmationRequest {
  email: string;
  token: string;
}

export interface ResendConfirmationEmailRequest {
  email: string;
}

export interface ResendConfirmationEmailResponse {
  message: string;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly csrf = inject(CsrfService);

  login(email: string, senha: string): Observable<void> {
    return this.postWithCsrf<LoginRequest, void>(
      '/api/auth/login',
      { email, senha },
      204,
      () => undefined,
    );
  }

  confirmarEmail(email: string, token: string): Observable<void> {
    return this.postWithCsrf<EmailConfirmationRequest, void>(
      '/api/auth/confirm-email',
      { email, token },
      204,
      () => undefined,
    );
  }

  reenviarConfirmacaoEmail(email: string): Observable<ResendConfirmationEmailResponse> {
    return this.postWithCsrf<ResendConfirmationEmailRequest, ResendConfirmationEmailResponse>(
      '/api/auth/resend-confirmation-email',
      { email },
      202,
      (response) => {
        if (!response.body || typeof response.body.message !== 'string') {
          throw new Error('Unexpected authentication response');
        }

        return response.body;
      },
    );
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
    return this.postWithCsrf<null, void>(
      '/api/auth/logout',
      null,
      204,
      () => undefined,
    );
  }

  private postWithCsrf<TRequest, TResponse>(
    url: string,
    body: TRequest | null,
    expectedStatus: number,
    getResponseValue: (response: HttpResponse<TResponse>) => TResponse,
  ): Observable<TResponse> {
    return this.csrf.getRequestToken().pipe(
      switchMap(({ requestToken }) =>
        this.http.post<TResponse>(url, body, {
          headers: new HttpHeaders({ 'X-XSRF-TOKEN': requestToken }),
          observe: 'response',
        }),
      ),
      map((response: HttpResponse<TResponse>) => {
        if (response.status !== expectedStatus) {
          throw new Error('Unexpected authentication response');
        }
        return getResponseValue(response);
      }),
    );
  }
}
