import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { catchError, map, of } from 'rxjs';
import { AtendimentoService } from '../core/atendimento.service';

export function linkWhatsApp(numero: string | null): string | null {
  return numero && /^\+[1-9]\d{7,14}$/.test(numero)
    ? `https://wa.me/${numero.slice(1)}?text=${encodeURIComponent('Olá! Gostaria de falar com a Xaquiamuni.')}`
    : null;
}

@Component({
  selector: 'app-contato-whatsapp',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (link(); as url) {
      <aside class="mx-auto w-full max-w-7xl px-4 py-3" aria-label="Contato com a loja">
        <a [href]="url" target="_blank" rel="noopener noreferrer">Falar com a loja pelo WhatsApp</a>
      </aside>
    }
  `,
})
export class ContatoWhatsAppComponent {
  protected readonly link = toSignal(inject(AtendimentoService).contato().pipe(
    map(contato => linkWhatsApp(contato.whatsApp)), catchError(() => of(null)),
  ));
}
