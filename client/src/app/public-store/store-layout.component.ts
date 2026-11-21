import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, of, timeout } from 'rxjs';
import { TemasLojaService } from '../core/temas-loja.service';
import { buscarManifestoTemaLoja, resolverTemaLoja, TEMA_ORIGINAL } from './manifestos-tema-loja';
import { RouterLink, RouterOutlet } from '@angular/router';
import { StoreFooterComponent } from './store-footer.component';
import { StoreHeaderComponent } from './store-header.component';
import { StorefrontContext } from './storefront-context';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, RouterOutlet, StoreFooterComponent, StoreHeaderComponent],
  providers: [StorefrontContext],
  selector: 'app-store-layout',
  host: { '[attr.data-theme]': 'tema()' },
  styleUrl: './store-layout.component.css',
  templateUrl: './store-layout.component.html',
})
export class StoreLayoutComponent {
  protected readonly contexto = inject(StorefrontContext);
  private readonly temaPublicado = signal(TEMA_ORIGINAL);
  protected readonly manifestoPrevia = computed(() => buscarManifestoTemaLoja(this.contexto.previewKey()));
  protected readonly tema = computed(() => this.manifestoPrevia()?.chave ?? this.temaPublicado());

  constructor() {
    if (this.contexto.preview) return;
    inject(TemasLojaService).consultarPublico().pipe(
      timeout(4000),
      catchError(() => of(null)),
      takeUntilDestroyed(),
    ).subscribe((publicacao) => this.temaPublicado.set(resolverTemaLoja(publicacao?.chave)));
  }
}
