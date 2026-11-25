import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, exhaustMap, filter, fromEvent, merge, of, timeout } from 'rxjs';
import { DOCUMENT } from '@angular/common';
import { TemasLojaService } from '../core/temas-loja.service';
import { RouterLink, RouterOutlet } from '@angular/router';
import { StoreFooterComponent } from './store-footer.component';
import { StoreHeaderComponent } from './store-header.component';
import { StoreCustomerSession } from './store-customer-session.service';
import { StorefrontContext } from './storefront-context';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, RouterOutlet, StoreFooterComponent, StoreHeaderComponent],
  providers: [StorefrontContext, StoreCustomerSession],
  selector: 'app-store-layout',
  host: { '[attr.data-theme]': 'contexto.tema()', '[style]': 'contexto.manifesto()?.sazonal?.tokens' },
  styleUrl: './store-layout.component.css',
  templateUrl: './store-layout.component.html',
})
export class StoreLayoutComponent {
  protected readonly contexto = inject(StorefrontContext);
  constructor() {
    if (this.contexto.preview) return;
    const api = inject(TemasLojaService);
    const document = inject(DOCUMENT);
    merge(
      of(null),
      fromEvent(document, 'visibilitychange').pipe(filter(() => document.visibilityState === 'visible')),
    ).pipe(
      exhaustMap(() => api.consultarPublico().pipe(timeout(4000), catchError(() => of(null)))),
      takeUntilDestroyed(),
    ).subscribe(publicacao => this.contexto.aplicarPublicado(publicacao?.chave));
  }
}
