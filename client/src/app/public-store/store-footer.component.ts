import { ChangeDetectionStrategy, inject, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { StorefrontContext } from './storefront-context';
import { StoreCustomerSession } from './store-customer-session.service';
import { ContatoLojaComponent } from './contato-loja.component';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, ContatoLojaComponent],
  selector: 'app-store-footer',
  styleUrl: './store-footer.component.css',
  templateUrl: './store-footer.component.html',
})
export class StoreFooterComponent {
  protected readonly contexto = inject(StorefrontContext);
  protected readonly conta = this.contexto.preview ? null : inject(StoreCustomerSession);
  protected readonly currentYear = new Date().getFullYear();
}
