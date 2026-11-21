import { DOCUMENT } from '@angular/common';
import { Injectable, inject, signal } from '@angular/core';

export type TemaAdmin = 'light' | 'dark';

const chavePreferencia = 'xaquiamuni:admin:tema';

@Injectable({ providedIn: 'root' })
export class TemaAdminService {
  private readonly documento = inject(DOCUMENT);
  private readonly preferencia = signal<TemaAdmin>(this.restaurar());
  readonly tema = this.preferencia.asReadonly();

  alternar(): void {
    const tema = this.tema() === 'light' ? 'dark' : 'light';
    this.preferencia.set(tema);
    try {
      this.documento.defaultView?.localStorage.setItem(chavePreferencia, tema);
    } catch {
      // A preferência permanece disponível em memória.
    }
  }

  private restaurar(): TemaAdmin {
    try {
      return this.documento.defaultView?.localStorage.getItem(chavePreferencia) === 'dark'
        ? 'dark'
        : 'light';
    } catch {
      return 'light';
    }
  }
}
