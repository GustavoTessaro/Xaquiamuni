import { ChangeDetectionStrategy, inject, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { StorefrontContext } from './storefront-context';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  selector: 'app-store-footer',
  styleUrl: './store-footer.component.css',
  templateUrl: './store-footer.component.html',
})
export class StoreFooterComponent {
  protected readonly contexto = inject(StorefrontContext);
  protected readonly currentYear = new Date().getFullYear();
}
