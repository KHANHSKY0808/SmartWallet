import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Subscription, interval } from 'rxjs';
import { ReportService } from '../../../core/services/report.service';
import { ServiceHealthItem } from '../../../core/models/report.models';

@Component({
  selector: 'app-admin-health',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div style="max-width: 100%; overflow-x: auto;">
      <div style="display: flex; justify-content: space-between; align-items: center; flex-wrap: wrap; gap: 1rem; margin-bottom: 1.5rem;">
        <div>
          <h2 style="margin: 0; font-size: 1.5rem; color: #1e293b;">🛡️ Giám sát Sức khỏe Hệ thống SOA</h2>
          <p style="margin: 0.25rem 0 0; color: #64748b; font-size: 0.9rem;">Theo dõi trạng thái 12 microservices theo thời gian thực (tự làm mới mỗi 10s)</p>
        </div>
        <button (click)="refresh()" [disabled]="loading()" style="padding: 0.5rem 1rem; background: #1e3a8a; color: white; border: none; border-radius: 6px; cursor: pointer;">
          {{ loading() ? 'Đang kiểm tra...' : 'Làm mới ngay' }}
        </button>
      </div>

      <div *ngIf="error()" style="padding: 1rem; background: #fef2f2; color: #b91c1c; border-radius: 8px; margin-bottom: 1rem;">
        {{ error() }}
      </div>

      <div style="background: white; border-radius: 10px; box-shadow: 0 1px 3px rgba(0,0,0,0.08); overflow-x: auto;">
        <table style="width: 100%; border-collapse: collapse; font-size: 0.9rem;">
          <thead>
            <tr style="background: #f8fafc; border-bottom: 2px solid #e2e8f0; text-align: left;">
              <th style="padding: 0.75rem 1rem;">Dịch vụ</th>
              <th style="padding: 0.75rem 1rem;">Trạng thái</th>
              <th style="padding: 0.75rem 1rem;">Độ trễ (ms)</th>
              <th style="padding: 0.75rem 1rem;">Lần kiểm cuối</th>
            </tr>
          </thead>
          <tbody>
            <tr *ngFor="let svc of services()" style="border-bottom: 1px solid #f1f5f9;">
              <td style="padding: 0.75rem 1rem; font-weight: 600;">{{ svc.service }}</td>
              <td style="padding: 0.75rem 1rem;">
                <span [style.background]="getStatusBg(svc.status)"
                      [style.color]="getStatusColor(svc.status)"
                      style="padding: 0.25rem 0.65rem; border-radius: 999px; font-weight: 600; font-size: 0.8rem;">
                  {{ svc.status }}
                </span>
              </td>
              <td style="padding: 0.75rem 1rem;">{{ svc.latencyMs }} ms</td>
              <td style="padding: 0.75rem 1rem; color: #64748b;">{{ svc.checkedAt | date:'HH:mm:ss dd/MM/yyyy' }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  `
})
export class AdminHealthComponent implements OnInit, OnDestroy {
  private reportService = inject(ReportService);
  private refreshSub?: Subscription;

  services = signal<ServiceHealthItem[]>([]);
  loading = signal(false);
  error = signal<string | null>(null);

  ngOnInit(): void {
    this.refresh();
    this.refreshSub = interval(10000).subscribe(() => this.refresh());
  }

  ngOnDestroy(): void {
    this.refreshSub?.unsubscribe();
  }

  refresh(): void {
    this.loading.set(true);
    this.error.set(null);
    this.reportService.getAdminHealth().subscribe({
      next: data => {
        this.services.set(data);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Không thể tải trạng thái hệ thống (yêu cầu quyền Admin).');
        this.loading.set(false);
      }
    });
  }

  getStatusBg(status: string): string {
    switch (status) {
      case 'Healthy': return '#dcfce7';
      case 'Degraded': return '#fef3c7';
      default: return '#fee2e2';
    }
  }

  getStatusColor(status: string): string {
    switch (status) {
      case 'Healthy': return '#166534';
      case 'Degraded': return '#92400e';
      default: return '#991b1b';
    }
  }
}

