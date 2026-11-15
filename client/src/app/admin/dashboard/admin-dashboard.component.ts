import { ChangeDetectionStrategy, Component } from '@angular/core';
import { AdminWorkspaceComponent } from '../admin-workspace.component';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AdminWorkspaceComponent],
  selector: 'app-admin-dashboard',
  styleUrl: './admin-dashboard.component.css',
  templateUrl: './admin-dashboard.component.html',
})
export class AdminDashboardComponent {}
