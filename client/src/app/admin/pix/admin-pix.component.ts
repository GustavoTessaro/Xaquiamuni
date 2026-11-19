import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import {
  DadosPix,
  PagamentoPixService,
  statusPix,
  tiposChavePix,
} from '../../core/pagamento-pix.service';
import { AdminWorkspaceComponent } from '../admin-workspace.component';

const formatos: Record<DadosPix['tipoChave'], RegExp> = {
  Aleatoria: /^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i,
  Email: /^(?=[!-~]+$)[^@]+@[^@.]+(?:\.[^@.]+)+$/,
  Telefone: /^\+[1-9][0-9]{1,14}$/,
};

@Component({
  selector: 'app-admin-pix',
  imports: [AdminWorkspaceComponent, ReactiveFormsModule, RouterLink],
  templateUrl: './admin-pix.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminPixComponent {
  private readonly api = inject(PagamentoPixService);
  private readonly destroy = inject(DestroyRef);
  protected readonly estado = signal<'carregando' | 'nova' | 'existente' | 'falha'>('carregando');
  protected readonly salvando = signal(false);
  protected readonly erro = signal('');
  protected readonly requerLogin = signal(false);
  protected readonly sucesso = signal('');
  protected readonly enviado = signal(false);
  protected readonly tipos = Object.entries(tiposChavePix);
  protected readonly formulario = new FormGroup(
    {
      tipoChave: new FormControl<DadosPix['tipoChave'] | ''>('', {
        nonNullable: true,
        validators: Validators.required,
      }),
      chavePix: new FormControl('', { nonNullable: true, validators: Validators.maxLength(77) }),
      nomeRecebedor: new FormControl('', {
        nonNullable: true,
        validators: [
          Validators.maxLength(150),
          (campo) =>
            !campo.value.trim() || /\p{Cc}/u.test(campo.value.trim()) ? { nome: true } : null,
        ],
      }),
      instrucoesAdicionais: new FormControl('', {
        nonNullable: true,
        validators: [
          Validators.maxLength(500),
          (campo) =>
            /\p{Cc}/u.test(campo.value.replace(/[\r\n\t]/g, '')) ? { controle: true } : null,
        ],
      }),
    },
    {
      validators: (grupo) => {
        const { tipoChave, chavePix } = grupo.getRawValue();
        const chave = chavePix.trim();
        return Object.hasOwn(formatos, tipoChave) &&
          formatos[tipoChave as DadosPix['tipoChave']].test(chave) &&
          chave !== '00000000-0000-0000-0000-000000000000'
          ? null
          : { chave: true };
      },
    },
  );

  constructor() {
    this.carregar();
  }

  protected carregar() {
    this.estado.set('carregando');
    this.erro.set('');
    this.requerLogin.set(false);
    this.api
      .consultarConfiguracao()
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe({
        next: (dados) => this.aplicar(dados),
        error: (erro: unknown) => {
          if (statusPix(erro) === 404) {
            this.estado.set('nova');
          } else {
            this.estado.set('falha');
            this.tratarFalha(erro, false);
          }
        },
      });
  }

  protected salvar() {
    if (this.salvando() || this.requerLogin() || !['nova', 'existente'].includes(this.estado()))
      return;
    this.enviado.set(true);
    this.erro.set('');
    this.sucesso.set('');
    const { tipoChave, chavePix, nomeRecebedor, instrucoesAdicionais } =
      this.formulario.getRawValue();
    if (!tipoChave || this.formulario.invalid) {
      this.erro.set('Revise os campos indicados no formulário.');
      return;
    }

    this.salvando.set(true);
    this.api
      .salvarConfiguracao({
        tipoChave,
        chavePix: chavePix.trim(),
        nomeRecebedor: nomeRecebedor.trim(),
        instrucoesAdicionais: instrucoesAdicionais.trim() || null,
      })
      .pipe(
        takeUntilDestroyed(this.destroy),
        finalize(() => this.salvando.set(false)),
      )
      .subscribe({
        next: (dados) => {
          this.aplicar(dados);
          this.sucesso.set('Configuração PIX salva.');
        },
        error: (erro: unknown) => this.tratarFalha(erro, true),
      });
  }

  private aplicar(dados: DadosPix) {
    this.formulario.reset({ ...dados, instrucoesAdicionais: dados.instrucoesAdicionais ?? '' });
    this.enviado.set(false);
    this.estado.set('existente');
  }

  private tratarFalha(erro: unknown, salvamento: boolean) {
    switch (statusPix(erro)) {
      case 401:
        this.requerLogin.set(true);
        this.erro.set('Entre novamente para acessar a configuração PIX.');
        break;
      case 403:
        this.erro.set('A conta atual não tem permissão para esta operação.');
        break;
      case 400:
        this.erro.set(
          erro instanceof HttpErrorResponse && erro.error?.errors
            ? 'Confira o tipo e o formato da chave, o nome e o tamanho das instruções.'
            : 'A validação de segurança da solicitação falhou. Tente novamente.',
        );
        break;
      default:
        this.erro.set(
          salvamento
            ? 'Não foi possível confirmar o salvamento. Tente novamente.'
            : 'Não foi possível consultar a configuração PIX. Tente novamente.',
        );
    }
  }
}
