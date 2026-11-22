import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { ComprovantePix, ComprovantesPixService, TentativaComprovantePix } from '../../core/comprovantes-pix.service';

const maximoBytes = 5 * 1024 * 1024;
const mimes: Record<string, string> = { pdf: 'application/pdf', jpg: 'image/jpeg', jpeg: 'image/jpeg', png: 'image/png' };
const tipos: Record<string, string> = { 'application/pdf': 'PDF', 'image/jpeg': 'JPEG', 'image/png': 'PNG' };

@Component({
  selector: 'app-comprovante-pix',
  imports: [DatePipe],
  templateUrl: './comprovante-pix.component.html',
  styleUrl: './comprovante-pix.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class.comprovante-admin]': 'admin()' },
})
export class ComprovantePixComponent {
  readonly pedidoId = input.required<string>();
  readonly admin = input(false);
  readonly podeEnviar = input(false);
  readonly pagamentoPendente = input(false);
  readonly revalidarPedido = output<void>();

  private readonly api = inject(ComprovantesPixService);
  private readonly destroy = inject(DestroyRef);
  private readonly revisao = signal(0);
  protected readonly comprovante = signal<ComprovantePix | null>(null);
  protected readonly consultando = signal(true);
  protected readonly consultaFalhou = signal(false);
  protected readonly enviando = signal(false);
  protected readonly baixando = signal(false);
  protected readonly arquivo = signal<File | null>(null);
  protected readonly confirmarSubstituicao = signal(false);
  protected readonly tentativa = signal<TentativaComprovantePix | null>(null);
  protected readonly mensagem = signal('');
  protected readonly erro = signal('');
  protected readonly tipos = tipos;
  protected readonly podeSelecionar = computed(() =>
    !this.admin() && this.podeEnviar() && !this.consultando() && !this.consultaFalhou() && !this.tentativa(),
  );

  constructor() {
    let escopoAnterior: string | undefined;
    effect((limpar) => {
      const id = this.pedidoId();
      const admin = this.admin();
      this.revisao();
      const escopo = `${admin}:${id}`;
      if (escopo !== escopoAnterior) {
        escopoAnterior = escopo;
        this.comprovante.set(null);
        this.limparSelecao();
        this.tentativa.set(null);
        this.mensagem.set('');
        this.erro.set('');
      }
      this.consultando.set(true);
      this.consultaFalhou.set(false);
      const consulta = this.api.consultar(id, admin).subscribe({
        next: comprovante => {
          this.comprovante.set(comprovante);
          this.consultando.set(false);
        },
        error: () => {
          this.consultaFalhou.set(true);
          this.consultando.set(false);
        },
      });
      limpar(() => consulta.unsubscribe());
    });
  }

  protected consultarNovamente(): void {
    this.revisao.update(valor => valor + 1);
  }

  protected tamanho(bytes: number): string {
    return new Intl.NumberFormat('pt-BR', { style: 'unit', unit: 'byte' }).format(bytes);
  }

  protected selecionar(evento: Event): void {
    const campo = evento.target as HTMLInputElement;
    const arquivo = campo.files?.[0];
    if (!arquivo || !this.podeSelecionar()) return;
    campo.value = '';
    this.limparSelecao();
    this.mensagem.set('');
    this.erro.set('');
    const extensao = /\.(pdf|jpe?g|png)$/i.exec(arquivo.name)?.[1].toLowerCase() ?? '';
    if (!mimes[extensao] || (arquivo.type && arquivo.type !== mimes[extensao]) || !arquivo.size) {
      this.erro.set('Selecione um arquivo PDF, JPG, JPEG ou PNG válido.');
    } else if (arquivo.size > maximoBytes) {
      this.erro.set('O arquivo deve ter até 5 MiB.');
    } else {
      this.arquivo.set(arquivo.type ? arquivo : new File([arquivo], arquivo.name, { type: mimes[extensao] }));
    }
  }

  protected limparSelecao(): void {
    this.arquivo.set(null);
    this.confirmarSubstituicao.set(false);
  }

  protected enviar(confirmado = false): void {
    const arquivo = this.arquivo();
    if (!arquivo || !this.podeSelecionar() || this.enviando()) return;
    const versao = this.comprovante()?.versao ?? null;
    if (versao && !confirmado) {
      this.confirmarSubstituicao.set(true);
      return;
    }
    const chave = globalThis.crypto?.randomUUID?.();
    if (!chave) {
      this.erro.set('Não foi possível iniciar o envio neste navegador.');
      return;
    }
    this.executar({ pedidoId: this.pedidoId(), arquivo, chave, versao });
  }

  protected recuperar(): void {
    const tentativa = this.tentativa();
    if (tentativa?.pedidoId === this.pedidoId() && !this.admin() && !this.enviando()) this.executar(tentativa);
  }

  private executar(tentativa: TentativaComprovantePix): void {
    const recuperacao = this.tentativa() !== null;
    this.enviando.set(true);
    this.erro.set('');
    this.mensagem.set('');
    this.confirmarSubstituicao.set(false);
    this.api.enviar(tentativa).pipe(
      takeUntilDestroyed(this.destroy),
      finalize(() => this.enviando.set(false)),
    ).subscribe({
      next: comprovante => {
        if (tentativa.pedidoId !== this.pedidoId()) return;
        this.tentativa.set(null);
        this.limparSelecao();
        this.comprovante.set(comprovante);
        this.mensagem.set(recuperacao ? 'Envio recuperado.' : 'Comprovante enviado.');
        this.consultarNovamente();
      },
      error: (erro: unknown) => {
        if (tentativa.pedidoId !== this.pedidoId()) return;
        const status = erro instanceof HttpErrorResponse ? erro.status : 0;
        if (status === 0 || status === 408 || status >= 500 || (recuperacao && status !== 409)) {
          this.tentativa.set(tentativa);
          this.erro.set(status === 401 || status === 403
            ? 'A sessão não permite recuperar este envio. Confira seu acesso antes de tentar novamente.'
            : 'Não foi possível confirmar o envio. Use “Recuperar envio” para verificar a tentativa com o mesmo arquivo.');
          return;
        }
        this.tentativa.set(null);
        if (status === 409) {
          this.limparSelecao();
          this.erro.set('O pedido ou o comprovante mudou. Confira a situação atual antes de enviar novamente.');
          this.consultarNovamente();
          this.revalidarPedido.emit();
        } else {
          this.erro.set(status === 413 ? 'O arquivo deve ter até 5 MiB.'
            : status === 415 ? 'O arquivo não passou na validação. Selecione outro comprovante.'
            : status === 401 || status === 403 ? 'Sua sessão não permite enviar este comprovante.'
            : status === 404 ? 'Pedido indisponível para esta conta.'
            : 'Não foi possível enviar o comprovante. Confira o arquivo e tente novamente.');
        }
      },
    });
  }

  protected baixar(): void {
    if (!this.comprovante() || this.baixando()) return;
    this.baixando.set(true);
    this.erro.set('');
    const id = this.pedidoId();
    this.api.baixar(id, this.admin()).pipe(
      takeUntilDestroyed(this.destroy),
      finalize(() => this.baixando.set(false)),
    ).subscribe({
      next: blob => {
        if (id !== this.pedidoId()) return;
        try { this.api.salvarDownload(blob); }
        catch { this.erro.set('O navegador não conseguiu salvar o arquivo.'); }
      },
      error: (erro: unknown) => {
        if (id !== this.pedidoId()) return;
        const status = erro instanceof HttpErrorResponse ? erro.status : 0;
        this.erro.set(status === 404 ? 'O comprovante não está mais disponível.'
          : status === 401 || status === 403 ? 'Sua sessão não permite baixar este comprovante.'
          : 'Não foi possível baixar o comprovante agora. Tente novamente.');
        if (status === 404) this.consultarNovamente();
      },
    });
  }
}
