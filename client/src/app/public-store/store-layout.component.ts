import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { StoreFooterComponent } from './store-footer.component';
import { StoreHeaderComponent } from './store-header.component';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, StoreFooterComponent, StoreHeaderComponent],
  selector: 'app-store-layout',
  styleUrl: './store-layout.component.css',
  templateUrl: './store-layout.component.html',
})
export class StoreLayoutComponent {}
