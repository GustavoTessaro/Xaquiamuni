import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { catchError, filter, fromEvent, map, merge, of, switchMap } from 'rxjs';
import { AtendimentoService } from '../core/atendimento.service';

export function linkWhatsApp(numero: string | null): string | null {
  return numero && /^\+[1-9]\d{7,14}$/.test(numero)
    ? `https://wa.me/${numero.slice(1)}?text=${encodeURIComponent('Olá! Gostaria de falar com a Xaquiamuni.')}`
    : null;
}

export function normalizarEmailComercial(endereco: string | null | undefined): string | null {
  if (!endereco || /[\u0000-\u001f\u007f-\u009f]/u.test(endereco)) return null;
  const valor = endereco.replace(/^ +| +$/g, '');
  const partes = /^([a-z0-9_+-]+(?:\.[a-z0-9_+-]+)*)@((?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63})$/i.exec(valor);
  return partes && valor.length <= 254 && partes[1].length <= 64
    ? partes[1] + '@' + partes[2].toLowerCase() : null;
}

export function linkEmailComercial(endereco: string | null | undefined): string | null {
  const email = normalizarEmailComercial(endereco);
  if (!email) return null;
  const [local, dominio] = email.split('@');
  return `mailto:${encodeURIComponent(local)}@${dominio}`;
}

@Component({
  selector: 'app-contato-loja',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (contatos(); as canais) {
      @if (canais.whatsApp || canais.email) {
        <section class="mt-4 grid gap-2" aria-label="Contato com a loja">
          <h2 class="font-semibold">Fale conosco</h2>
          @if (canais.whatsApp) {
            <a class="inline-flex w-fit items-center gap-2 underline" [href]="canais.whatsApp" target="_blank" rel="noopener noreferrer">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" aria-hidden="true">
                <path d="M4 19l1-4a9 9 0 1 1 4 4l-5 1zM8 12h8"/>
              </svg>
              WhatsApp
            </a>
          }
          @if (canais.email) {
            <a class="inline-flex w-fit items-center gap-2 underline" [href]="canais.email">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" aria-hidden="true">
                <rect x="3" y="5" width="18" height="14" rx="2"/><path d="m3 7 9 6 9-6"/>
              </svg>
              E-mail comercial
            </a>
          }
        </section>
      }
    }
  `,
})
export class ContatoLojaComponent {
  private readonly api = inject(AtendimentoService);
  private readonly documento = inject(DOCUMENT);
  protected readonly contatos = toSignal(merge(
    of(null),
    inject(Router).events.pipe(filter(evento => evento instanceof NavigationEnd)),
    fromEvent(this.documento, 'visibilitychange').pipe(filter(() => this.documento.visibilityState === 'visible')),
  ).pipe(
    switchMap(() => this.api.contato().pipe(
      map(contato => ({ whatsApp: linkWhatsApp(contato.whatsApp), email: linkEmailComercial(contato.emailComercial) })),
      catchError(() => of(null)),
    )),
  ));
}
