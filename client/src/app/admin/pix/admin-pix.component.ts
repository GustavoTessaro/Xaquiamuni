import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import {
  AlteracaoPix,
  ConfiguracaoPix,
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
  protected readonly revisaoRecarregada = signal<ConfiguracaoPix | null>(null);
  protected readonly conflito = signal(false);
  protected readonly pendentes = signal(0);
  protected readonly confirmarPendentes = signal(false);
  private revisao = 0;
  private tentativa?: { dados: AlteracaoPix; chave: string };
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
    if (this.salvando() || this.salvamentoIncerto()) return;
    this.estado.set('carregando');
    this.erro.set('');
    this.requerLogin.set(false);
    this.api
      .consultarConfiguracao()
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe({
        next: (dados) => {
          if (this.conflito()) {
            this.revisaoRecarregada.set(dados);
            this.revisao = dados.revisao;
            this.pendentes.set(dados.pedidosPendentes);
            this.confirmarPendentes.set(false);
            this.conflito.set(false);
            this.estado.set(dados.pix ? 'existente' : 'nova');
            this.erro.set('Revisão atual carregada. Confira os dados digitados antes de salvar novamente.');
          } else {
            this.aplicar(dados);
          }
        },
        error: (erro: unknown) => {
          this.estado.set('falha');
          this.tratarFalha(erro, false);
        },
      });
  }

  protected salvar() {
    if (this.salvando() || this.salvamentoIncerto() || this.conflito() || this.requerLogin() || !['nova', 'existente'].includes(this.estado()))
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

    if (this.pendentes() > 0 && !this.confirmarPendentes()) {
      this.erro.set('Confira os pedidos pendentes e marque a confirmação antes de salvar.');
      return;
    }
    this.tentativa = {
      chave: crypto.randomUUID(),
      dados: {
        tipoChave,
        chavePix: chavePix.trim(),
        nomeRecebedor: nomeRecebedor.trim(),
        instrucoesAdicionais: instrucoesAdicionais.trim() || null,
        revisao: this.revisao,
        confirmarPendentes: this.confirmarPendentes(),
      },
    };
    this.enviar();
  }

  protected recuperar() {
    if (this.salvamentoIncerto()) this.enviar();
  }

  private enviar() {
    if (this.salvando() || !this.tentativa) return;
    const { dados, chave } = this.tentativa;
    const recuperando = this.salvamentoIncerto();
    this.erro.set('');
    this.salvando.set(true);
    this.api.salvarConfiguracao(dados, chave)
      .pipe(
        takeUntilDestroyed(this.destroy),
        finalize(() => this.salvando.set(false)),
      )
      .subscribe({
        next: (dados) => {
          this.tentativa = undefined;
          this.salvamentoIncerto.set(false);
          this.aplicar(dados);
          this.sucesso.set(recuperando ? 'Salvamento original confirmado.' : 'Configuração PIX salva.');
          if (recuperando) {
            this.salvando.set(false);
            this.carregar();
          }
        },
        error: (erro: unknown) => this.tratarFalha(erro, true),
      });
  }

  private aplicar(config: ConfiguracaoPix) {
    this.revisaoRecarregada.set(null);
    const { pix } = config;
    this.revisao = config.revisao;
    this.pendentes.set(config.pedidosPendentes);
    this.confirmarPendentes.set(false);
    this.formulario.reset({
      tipoChave: pix?.tipoChave ?? '',
      chavePix: pix?.chavePix ?? '',
      nomeRecebedor: pix?.nomeRecebedor ?? '',
      instrucoesAdicionais: pix?.instrucoesAdicionais ?? '',
    });
    this.enviado.set(false);
    this.estado.set(pix ? 'existente' : 'nova');
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
        this.tentativa = undefined;
        this.salvamentoIncerto.set(false);
        this.conflito.set(true);
        this.erro.set(erro instanceof HttpErrorResponse && erro.error?.codigo === 'pix_confirmacao_pendente'
          ? 'Há pedidos aguardando pagamento. Recarregue a situação dos pedidos antes de continuar.'
          : 'A configuração mudou ou a operação entrou em conflito. Recarregue a revisão para continuar.');
        break;
      default:
        if (salvamento) this.salvamentoIncerto.set(true);
        this.erro.set(
          salvamento
            ? 'Não foi possível confirmar o salvamento. Recupere o salvamento usando a operação original.'
            : 'Não foi possível consultar a configuração PIX. Tente novamente.',
        );
    }
  }
}
