import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  selector: 'app-store-footer',
  styleUrl: './store-footer.component.css',
  templateUrl: './store-footer.component.html',
})
export class StoreFooterComponent {
  protected readonly currentYear = new Date().getFullYear();
}
