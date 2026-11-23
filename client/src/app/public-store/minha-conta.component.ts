import { DOCUMENT } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { Subject, catchError, fromEvent, map, merge, of, startWith, switchMap } from 'rxjs';
import { PerfilClienteService, type ResumoPerfilCliente } from '../core/perfil-cliente.service';

type Estado =
  | { tipo: 'carregando' | 'oculto' | 'sessao' | 'negado' | 'erro' }
  | { tipo: 'pronto'; perfil: ResumoPerfilCliente };

@Component({
  selector: 'app-minha-conta',
  imports: [RouterLink],
  templateUrl: './minha-conta.component.html',
  styleUrl: './minha-conta.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MinhaContaComponent {
  private readonly perfil = inject(PerfilClienteService);
  private readonly documento = inject(DOCUMENT);
  protected readonly recarregar = new Subject<void>();

  protected readonly estado = toSignal(merge(this.recarregar, fromEvent(this.documento, 'visibilitychange')).pipe(
    startWith(null),
    switchMap(() => {
      if (this.documento.hidden) return of<Estado>({ tipo: 'oculto' });

      return this.perfil.consultar().pipe(
        map((perfil): Estado => ({ tipo: 'pronto', perfil })),
        startWith<Estado>({ tipo: 'carregando' }),
        catchError((erro: unknown) => of<Estado>({
          tipo: erro instanceof HttpErrorResponse && erro.status === 401 ? 'sessao'
            : erro instanceof HttpErrorResponse && erro.status === 403 ? 'negado' : 'erro',
        })),
      );
    }),
  ), { initialValue: { tipo: 'carregando' } as Estado });
}
