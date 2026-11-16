import { ChangeDetectionStrategy, Component } from '@angular/core';
import { CatalogoProdutosComponent } from './catalogo-produtos.component';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CatalogoProdutosComponent],
  selector: 'app-catalogo-publico',
  template: '<app-catalogo-produtos />',
})
export class CatalogoPublicoComponent {}
