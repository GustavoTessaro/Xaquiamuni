import { HttpClient, HttpErrorResponse, HttpHeaders, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, map, of, switchMap, throwError } from 'rxjs';
import { CsrfService } from '../security/csrf.service';

export interface AdminSession {
  userId: string;
}

interface AdminLoginRequest {
  email: string;
  senha: string;
}

@Injectable({ providedIn: 'root' })
export class AdminAuthService {
  private readonly http = inject(HttpClient);
  private readonly csrf = inject(CsrfService);

  login(email: string, senha: string): Observable<void> {
    return this.postWithCsrf<AdminLoginRequest, void>(
      '/api/admin/auth/login',
      { email, senha },
      204,
      () => undefined,
    );
  }

  getCurrentSession(): Observable<AdminSession | null> {
    return this.http.get<AdminSession>('/api/admin/auth/me').pipe(
      catchError((error: unknown) =>
        error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403)
          ? of(null)
          : throwError(() => error),
      ),
    );
  }

  logout(): Observable<void> {
    return this.postWithCsrf<null, void>(
      '/api/admin/auth/logout',
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
          throw new Error('Unexpected administrator authentication response');
        }
        return getResponseValue(response);
      }),
    );
  }
}
