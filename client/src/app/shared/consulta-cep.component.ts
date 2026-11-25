import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, effect, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subscription } from 'rxjs';

import { CamposCep, ConsultaCepService, mensagensCep } from '../core/consulta-cep.service';

@Component({
  selector: 'app-consulta-cep',
  template: `
    <button type="button" class="min-h-11 rounded-lg border border-current px-3 text-sm font-semibold focus-visible:outline-2 disabled:opacity-60"
      (click)="consultar()" [disabled]="carregando()" [attr.aria-busy]="carregando()">
      {{ carregando() ? 'Consultando CEP…' : 'Consultar CEP' }}
    </button>
    @if (erro()) { <p role="alert" class="text-sm">{{ erro() }}</p> }
    @if (aviso()) { <p role="status">{{ aviso() }}</p> }
  `,
})
export class ConsultaCepComponent {
  readonly cep = input.required<string>();
  readonly contexto = input('');
  readonly endereco = output<CamposCep>();
  private readonly consulta = inject(ConsultaCepService);
  private readonly destroy = inject(DestroyRef);
  private requisicao?: Subscription;
  protected readonly carregando = signal(false);
  protected readonly erro = signal('');
  protected readonly aviso = signal('');

  constructor() {
    effect(() => {
      this.cep();
      this.contexto();
      this.requisicao?.unsubscribe();
      this.carregando.set(false);
      this.erro.set('');
      this.aviso.set('');
    });
  }

  protected consultar(): void {
    const cep = this.cep().trim();
    const contexto = this.contexto();
    if (this.carregando()) return;
    this.erro.set('');
    this.aviso.set('');
    if (!/^\d{5}-?\d{3}$/.test(cep)) {
      this.erro.set(mensagensCep['cep_invalido']);
      return;
    }
    this.carregando.set(true);
    this.requisicao = this.consulta.consultar(cep)
      .pipe(takeUntilDestroyed(this.destroy)).subscribe({
        next: (endereco) => {
          if (cep !== this.cep().trim() || contexto !== this.contexto()) return;
          this.carregando.set(false);
          this.endereco.emit({
            uf: endereco.uf, cidade: endereco.cidade,
            ...(endereco.bairro ? { bairro: endereco.bairro } : {}),
            ...(endereco.logradouro ? { logradouro: endereco.logradouro } : {}),
          });
          this.aviso.set('Endereço atualizado. Revise os dados e complete os campos restantes.');
        },
        error: (erro: unknown) => {
          if (cep !== this.cep().trim() || contexto !== this.contexto()) return;
          this.carregando.set(false);
          this.erro.set(erro instanceof HttpErrorResponse && erro.status === 429
            ? 'Muitas consultas de CEP. Aguarde antes de tentar novamente.'
            : mensagensCep[erro instanceof HttpErrorResponse ? erro.error?.codigo : ''] ?? mensagensCep['cep_indisponivel']);
        },
      });
  }
}
