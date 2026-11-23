import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import type { DadosEntrega, ModalidadeEntrega } from '../core/atendimento.service';

@Component({
  selector: 'app-dados-entrega',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="wrap-anywhere">
      <strong>{{ modalidade() === 'EntregaPropria' ? 'Entrega própria' : 'Retirada na loja' }}</strong>
      @if (dados()?.destino; as destino) {
        <p>{{ destino.logradouro }}, {{ destino.numero }}{{ destino.complemento ? ' · ' + destino.complemento : '' }}</p>
        <p>{{ destino.bairro }} · {{ destino.cidade }}/{{ destino.uf }} · CEP {{ destino.cep }}</p>
      } @else {
        @if (dados()?.enderecoRetirada; as endereco) { <p>{{ endereco }}</p> }
        @if (dados()?.instrucoesRetirada; as instrucoes) { <p>{{ instrucoes }}</p> }
      }
    </div>
  `,
})
export class DadosEntregaComponent {
  readonly modalidade = input.required<ModalidadeEntrega>();
  readonly dados = input<DadosEntrega | null>();
}
