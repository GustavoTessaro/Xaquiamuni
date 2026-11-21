import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, of, timeout } from 'rxjs';
import { TemasLojaService } from '../core/temas-loja.service';
import { resolverTemaLoja, TEMA_ORIGINAL } from './manifestos-tema-loja';
import { RouterOutlet } from '@angular/router';
import { StoreFooterComponent } from './store-footer.component';
import { StoreHeaderComponent } from './store-header.component';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, StoreFooterComponent, StoreHeaderComponent],
  selector: 'app-store-layout',
  host: { '[attr.data-theme]': 'tema()' },
  styleUrl: './store-layout.component.css',
  templateUrl: './store-layout.component.html',
})
export class StoreLayoutComponent {
  protected readonly tema = signal(TEMA_ORIGINAL);

  constructor() {
    inject(TemasLojaService).consultarPublico().pipe(
      timeout(4000),
      catchError(() => of(null)),
      takeUntilDestroyed(),
    ).subscribe((publicacao) => this.tema.set(resolverTemaLoja(publicacao?.chave)));
  }
}
