import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export interface CsrfTokenResponse {
  requestToken: string;
}

@Injectable({ providedIn: 'root' })
export class CsrfService {
  private readonly http = inject(HttpClient);

  getRequestToken(): Observable<CsrfTokenResponse> {
    return this.http.get<CsrfTokenResponse>('/api/auth/csrf');
  }
}
