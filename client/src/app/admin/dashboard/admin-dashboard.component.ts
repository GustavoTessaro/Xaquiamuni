import { RouterLink } from '@angular/router';
import { ChangeDetectionStrategy, Component } from '@angular/core';
import { AdminWorkspaceComponent } from '../admin-workspace.component';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AdminWorkspaceComponent, RouterLink],
  selector: 'app-admin-dashboard',
  styleUrl: './admin-dashboard.component.css',
  templateUrl: './admin-dashboard.component.html',
})
export class AdminDashboardComponent {}
