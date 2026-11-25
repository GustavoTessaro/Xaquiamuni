import { DOCUMENT } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { Subject, catchError, filter, finalize, fromEvent, merge, of, startWith, switchMap, timer } from 'rxjs';
import { AuthService, AuthSession } from '../core/auth/auth.service';
import { validarDestinoLoja } from '../core/auth/destino-login-cliente';

@Injectable({ providedIn: 'root' })
export class StoreCustomerSession {
  private readonly destroyRef = inject(DestroyRef);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly document = inject(DOCUMENT);
  private readonly invalidacao = new Subject<true>();

  readonly session = signal<AuthSession | null>(null);
  readonly loading = signal(true);
  readonly sessionError = signal('');
  readonly signingOut = signal(false);
  readonly logoutError = signal('');
  readonly destino = signal(this.destinoSeguro(this.router.url));

  constructor() {
    const navegacao = this.router.events.pipe(filter(event => event instanceof NavigationEnd));
    navegacao.pipe(takeUntilDestroyed()).subscribe(event =>
      this.destino.set(this.destinoSeguro(event.urlAfterRedirects)));
    merge(
      navegacao,
      fromEvent(this.document, 'visibilitychange').pipe(filter(() => !this.document.hidden)),
      timer(60_000, 60_000).pipe(filter(() => !!this.session() && !this.document.hidden)),
      this.invalidacao,
    ).pipe(
      startWith(null),
      filter(event => event === true || !this.signingOut()),
      switchMap(event => {
        this.sessionError.set('');
        return event === true ? of(null) : this.auth.obterSessao().pipe(
          catchError((error: unknown) => {
            if (!(error instanceof HttpErrorResponse && error.status === 403)) {
              this.sessionError.set('Não foi possível verificar sua sessão. Tente entrar novamente.');
            }
            return of(null);
          }),
        );
      }),
      takeUntilDestroyed(),
    ).subscribe(session => {
      this.session.set(session);
      this.loading.set(false);
    });
  }

  invalidar(): void {
    this.invalidacao.next(true);
  }

  sair(): void {
    if (!this.session() || this.signingOut()) return;
    this.signingOut.set(true);
    this.logoutError.set('');
    this.auth.logout().pipe(
      finalize(() => this.signingOut.set(false)),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: () => {
        this.invalidar();
        void this.router.navigateByUrl('/');
      },
      error: () => this.logoutError.set('Não foi possível encerrar a sessão. Tente novamente.'),
    });
  }

  private destinoSeguro(url: string): string {
    return this.router.serializeUrl(validarDestinoLoja(url));
  }
}
