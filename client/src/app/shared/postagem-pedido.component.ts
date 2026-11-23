import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, input, signal } from '@angular/core';
import type { PostagemPostal } from '../public-store/pedidos.service';

@Component({
  selector: 'app-postagem-pedido',
  imports: [DatePipe],
  template: `
    @if (postagem(); as dados) {
      <section class="grid gap-2 wrap-anywhere" aria-label="Acompanhamento postal">
        <p>Postado em {{ dados.postadaEmUtc | date:'dd/MM/yyyy HH:mm' }} · Serviço utilizado: {{ dados.servicoPostalUtilizado }}</p>
        @if (dados.codigoRastreamento; as codigo) {
          <p>Código de rastreamento: <strong>{{ codigo }}</strong></p>
          <button class="underline justify-self-start py-2" type="button" (click)="copiar(codigo)">Copiar código</button>
          @if (dados.urlConsultaRastreamento; as url) {
            @if (portalOficial(url)) {
              <a [href]="url" target="_blank" rel="noopener noreferrer" class="underline">Consultar no portal dos Correios (nova aba)</a>
            }
          }
        } @else { <p>Rastreamento não informado.</p> }
        @if (dados.entregueEmUtc) { <p>Entrega confirmada em {{ dados.entregueEmUtc | date:'dd/MM/yyyy HH:mm' }}.</p> }
        @if (mensagem()) { <p role="status">{{ mensagem() }}</p> }
      </section>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PostagemPedidoComponent {
  readonly postagem = input<PostagemPostal | null>();
  protected readonly mensagem = signal('');

  protected portalOficial(url: string) {
    return /^https:\/\/rastreamento\.correios\.com\.br\/app\/index\.php$/.test(url);
  }

  protected async copiar(codigo: string) {
    try {
      await navigator.clipboard.writeText(codigo);
      this.mensagem.set('Código copiado.');
    } catch {
      this.mensagem.set('Selecione o código exibido para copiá-lo.');
    }
  }
}

