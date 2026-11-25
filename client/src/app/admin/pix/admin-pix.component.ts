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

function cnpjValido(chave: string): boolean {
  const formato =
    /^(?:[A-Za-z0-9]{12}[0-9]{2}|[A-Za-z0-9]{2}\.[A-Za-z0-9]{3}\.[A-Za-z0-9]{3}\/[A-Za-z0-9]{4}-[0-9]{2})$/;
  if (!formato.test(chave)) return false;
  const cnpj = chave.replace(/[.\/-]/g, '').toUpperCase();
  if (cnpj === '00000000000000') return false;

  const digito = (base: string) => {
    let soma = 0;
    for (let i = 0; i < base.length; i++) {
      soma += (base.charCodeAt(base.length - 1 - i) - 48) * (2 + (i % 8));
    }
    const resto = soma % 11;
    return resto < 2 ? 0 : 11 - resto;
  };
  return (
    Number(cnpj[12]) === digito(cnpj.slice(0, 12)) && Number(cnpj[13]) === digito(cnpj.slice(0, 13))
  );
}

const formatos: Record<Exclude<DadosPix['tipoChave'], 'Cnpj'>, RegExp> = {
  Aleatoria: /^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i,
  Email: /^(?=[!-~]+$)[^@]+@[^@.]+(?:\.[^@.]+)+$/,
  Telefone: /^\+[1-9][0-9]{1,14}$/,
};

@Component({
  selector: 'app-admin-pix',
  imports: [AdminWorkspaceComponent, ReactiveFormsModule, RouterLink],
  templateUrl: './admin-pix.component.html',
  styleUrl: '../admin-atendimento.component.css',
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
  protected readonly salvamentoIncerto = signal(false);
  protected readonly tipos = Object.entries(tiposChavePix);
  protected readonly tiposChave = tiposChavePix;
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
        const valido =
          tipoChave === 'Cnpj'
            ? cnpjValido(chave)
            : tipoChave &&
              Object.hasOwn(formatos, tipoChave) &&
              formatos[tipoChave as keyof typeof formatos].test(chave) &&
              chave !== '00000000-0000-0000-0000-000000000000';
        return valido ? null : { chave: true };
      },
    },
  );

  constructor() {
    this.carregar();
  }

  protected carregar() {
    if (this.salvando()) return;
    this.estado.set('carregando');
    this.erro.set('');
    this.requerLogin.set(false);
    this.api
      .consultarConfiguracao()
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe({
        next: (dados) => {
          if (this.salvamentoIncerto()) this.conferirSalvamento(dados);
          else this.aplicar(dados);
        },
        error: (erro: unknown) => {
          if (statusPix(erro) === 404) {
            this.estado.set('nova');
            this.salvamentoIncerto.set(false);
          } else {
            this.estado.set('falha');
            this.tratarFalha(erro, false);
          }
        },
      });
  }

  protected salvar() {
    if (this.salvando() || this.salvamentoIncerto() || this.requerLogin() || !['nova', 'existente'].includes(this.estado()))
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

  private conferirSalvamento(dados: DadosPix): void {
    this.salvamentoIncerto.set(false);
    this.aplicar(dados);
    this.sucesso.set('Configuração atual carregada. Confira os dados antes de salvar outra alteração.');
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
      case 409:
        if (salvamento) this.salvamentoIncerto.set(true);
        this.erro.set('A configuração pode ter mudado. Consulte os dados salvos antes de continuar.');
        break;
      default:
        if (salvamento) this.salvamentoIncerto.set(true);
        this.erro.set(
          salvamento
            ? 'Não foi possível confirmar o salvamento. Consulte a configuração salva antes de tentar novamente.'
            : 'Não foi possível consultar a configuração PIX. Tente novamente.',
        );
    }
  }
}
