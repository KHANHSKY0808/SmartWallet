import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { BaseChartDirective } from 'ng2-charts';
import { ChartConfiguration, ChartData } from 'chart.js';
import { Subscription, interval, switchMap, takeWhile } from 'rxjs';
import { ReportService } from '../../../core/services/report.service';
import {
  AnalysisResponse,
  DashboardResponse,
  ExportJobDto
} from '../../../core/models/report.models';

@Component({
  selector: 'app-reports-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule, BaseChartDirective],
  template: `
    <div class="reports-container" style="max-width: 100%; overflow-x: hidden;">
      <div style="display: flex; justify-content: space-between; align-items: center; flex-wrap: wrap; gap: 1rem; margin-bottom: 1.5rem;">
        <div>
          <h2 style="margin: 0; font-size: 1.5rem; color: #1e293b;">📊 Báo cáo & Phân tích Tài chính</h2>
          <p style="margin: 0.25rem 0 0; color: #64748b; font-size: 0.9rem;">Tổng quan thu chi, xu hướng theo kỳ và xuất báo cáo Excel/PDF</p>
        </div>

        <!-- F-02: Bộ lọc kỳ Dashboard -->
        <div style="display: flex; gap: 0.5rem; align-items: center; flex-wrap: wrap;">
          <label style="font-size: 0.85rem; color: #475569;">Tháng:</label>
          <select [(ngModel)]="selectedMonth" (ngModelChange)="onPeriodChange()" style="padding: 0.4rem 0.6rem; border: 1px solid #cbd5e1; border-radius: 6px;">
            <option *ngFor="let m of months" [ngValue]="m">Tháng {{ m }}</option>
          </select>
          <label style="font-size: 0.85rem; color: #475569;">Năm:</label>
          <input type="number" [(ngModel)]="selectedYear" (ngModelChange)="onPeriodChange()" min="2000" max="2100" style="width: 85px; padding: 0.4rem 0.6rem; border: 1px solid #cbd5e1; border-radius: 6px;" />
        </div>
      </div>

      <!-- Loading & Error states (F-01) -->
      <div *ngIf="dashboardLoading()" style="padding: 2rem; text-align: center; color: #64748b; background: white; border-radius: 10px;">
        Đang tải dữ liệu báo cáo...
      </div>

      <div *ngIf="dashboardError()" style="padding: 1rem; background: #fef2f2; color: #b91c1c; border: 1px solid #fecaca; border-radius: 8px; margin-bottom: 1rem;">
        {{ dashboardError() }}
      </div>

      <ng-container *ngIf="dashboard() as d">
        <!-- Excluded Currencies warning -->
        <div *ngIf="d.excludedCurrencies.length > 0" style="padding: 0.75rem 1rem; background: #fffbeb; color: #92400e; border: 1px solid #fde68a; border-radius: 8px; margin-bottom: 1rem; font-size: 0.9rem;">
          ⚠️ Các giao dịch ngoại tệ sau chưa có tỷ giá quy đổi VND nên tạm không tính vào tổng: <strong>{{ d.excludedCurrencies.join(', ') }}</strong>
        </div>

        <!-- Summary Cards (F-01) -->
        <div style="display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 1rem; margin-bottom: 1.5rem;">
          <div style="background: white; padding: 1.25rem; border-radius: 10px; box-shadow: 0 1px 3px rgba(0,0,0,0.08); border-left: 4px solid #16a34a;">
            <div style="font-size: 0.85rem; color: #64748b;">Tổng thu tháng {{ d.month }}/{{ d.year }}</div>
            <div style="font-size: 1.4rem; font-weight: 700; color: #16a34a; margin-top: 0.3rem;">{{ d.totalIncome | number:'1.0-0' }} VND</div>
          </div>
          <div style="background: white; padding: 1.25rem; border-radius: 10px; box-shadow: 0 1px 3px rgba(0,0,0,0.08); border-left: 4px solid #dc2626;">
            <div style="font-size: 0.85rem; color: #64748b;">Tổng chi tháng {{ d.month }}/{{ d.year }}</div>
            <div style="font-size: 1.4rem; font-weight: 700; color: #dc2626; margin-top: 0.3rem;">{{ d.totalExpense | number:'1.0-0' }} VND</div>
          </div>
          <div style="background: white; padding: 1.25rem; border-radius: 10px; box-shadow: 0 1px 3px rgba(0,0,0,0.08); border-left: 4px solid #2563eb;">
            <div style="font-size: 0.85rem; color: #64748b;">Chênh lệch ròng</div>
            <div style="font-size: 1.4rem; font-weight: 700; color: #1e40af; margin-top: 0.3rem;">{{ d.net | number:'1.0-0' }} VND</div>
          </div>
        </div>

        <!-- Empty state (F-01) -->
        <div *ngIf="d.totalIncome === 0 && d.totalExpense === 0" style="padding: 2rem; text-align: center; background: white; border-radius: 10px; color: #64748b; margin-bottom: 1.5rem;">
          Chưa có giao dịch thu/chi nào trong tháng {{ d.month }}/{{ d.year }}.
        </div>

        <!-- Charts Grid (F-01, F-06 responsive) -->
        <div style="display: grid; grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)); gap: 1.5rem; margin-bottom: 1.5rem;">
          <div style="background: white; padding: 1.25rem; border-radius: 10px; box-shadow: 0 1px 3px rgba(0,0,0,0.08);">
            <h3 style="margin: 0 0 1rem; font-size: 1.05rem;">Cơ cấu chi tiêu theo danh mục</h3>
            <div *ngIf="d.byCategory.length === 0" style="color: #94a3b8; text-align: center; padding: 2rem 0;">Chưa có dữ liệu chi tiêu</div>
            <div *ngIf="d.byCategory.length > 0" style="max-height: 280px; display: flex; justify-content: center;">
              <canvas baseChart [data]="pieChartData" [type]="'doughnut'" [options]="pieChartOptions"></canvas>
            </div>
          </div>

          <div style="background: white; padding: 1.25rem; border-radius: 10px; box-shadow: 0 1px 3px rgba(0,0,0,0.08);">
            <h3 style="margin: 0 0 1rem; font-size: 1.05rem;">Biểu đồ Thu - Chi theo kỳ</h3>
            <div style="max-height: 280px;">
              <canvas baseChart [data]="barChartData" [type]="'bar'" [options]="barChartOptions"></canvas>
            </div>
          </div>
        </div>

        <!-- Top Expenses & Budget Progress -->
        <div style="display: grid; grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)); gap: 1.5rem; margin-bottom: 1.5rem;">
          <div style="background: white; padding: 1.25rem; border-radius: 10px; box-shadow: 0 1px 3px rgba(0,0,0,0.08); overflow-x: auto;">
            <h3 style="margin: 0 0 1rem; font-size: 1.05rem;">Top 5 khoản chi lớn nhất</h3>
            <p *ngIf="d.topExpenses.length === 0" style="color: #94a3b8;">Không có khoản chi nào.</p>
            <table *ngIf="d.topExpenses.length > 0" style="width: 100%; border-collapse: collapse; font-size: 0.9rem;">
              <tr *ngFor="let item of d.topExpenses" style="border-bottom: 1px solid #f1f5f9;">
                <td style="padding: 0.5rem 0;">
                  <div style="font-weight: 600;">{{ item.categoryName }}</div>
                  <div style="font-size: 0.8rem; color: #64748b;">{{ item.note || 'Không có ghi chú' }}</div>
                </td>
                <td style="padding: 0.5rem 0; text-align: right; font-weight: 600; color: #dc2626;">
                  {{ item.amount | number:'1.0-0' }} VND
                </td>
              </tr>
            </table>
          </div>

          <div style="background: white; padding: 1.25rem; border-radius: 10px; box-shadow: 0 1px 3px rgba(0,0,0,0.08);">
            <h3 style="margin: 0 0 1rem; font-size: 1.05rem;">Tiến độ ngân sách</h3>
            <div *ngIf="d.budgetsUnavailable" style="color: #b45309; background: #fffbeb; padding: 0.75rem; border-radius: 6px; font-size: 0.85rem;">
              Dịch vụ ngân sách tạm thời không phản hồi. Vui lòng thử lại sau.
            </div>
            <div *ngIf="!d.budgetsUnavailable && (!d.budgets || d.budgets.length === 0)" style="color: #94a3b8;">
              Chưa thiết lập ngân sách cho tháng này.
            </div>
            <div *ngFor="let b of d.budgets || []" style="margin-bottom: 0.75rem;">
              <div style="display: flex; justify-content: space-between; font-size: 0.85rem; margin-bottom: 0.25rem;">
                <span>{{ b.categoryName }}</span>
                <span>{{ b.spent | number:'1.0-0' }} / {{ b.limit | number:'1.0-0' }} ({{ b.percent }}%)</span>
              </div>
              <div style="height: 8px; background: #e2e8f0; border-radius: 4px; overflow: hidden;">
                <div [style.width.%]="Math.min(b.percent, 100)" [style.background]="b.percent >= 100 ? '#dc2626' : (b.percent >= 80 ? '#f59e0b' : '#2563eb')" style="height: 100%;"></div>
              </div>
            </div>
          </div>
        </div>
      </ng-container>

      <!-- Phân tích & So sánh kỳ (F-03) -->
      <div style="background: white; padding: 1.25rem; border-radius: 10px; box-shadow: 0 1px 3px rgba(0,0,0,0.08); margin-bottom: 1.5rem;">
        <div style="display: flex; justify-content: space-between; align-items: center; flex-wrap: wrap; gap: 0.75rem; margin-bottom: 1rem;">
          <h3 style="margin: 0; font-size: 1.05rem;">📈 Phân tích xu hướng & So sánh kỳ trước</h3>
          <div style="display: flex; gap: 0.5rem; flex-wrap: wrap; align-items: center;">
            <input type="date" [(ngModel)]="analysisFrom" style="padding: 0.35rem 0.5rem; border: 1px solid #cbd5e1; border-radius: 6px;" />
            <span>đến</span>
            <input type="date" [(ngModel)]="analysisTo" style="padding: 0.35rem 0.5rem; border: 1px solid #cbd5e1; border-radius: 6px;" />
            <select [(ngModel)]="analysisGroupBy" style="padding: 0.35rem 0.5rem; border: 1px solid #cbd5e1; border-radius: 6px;">
              <option value="day">Theo ngày</option>
              <option value="week">Theo tuần</option>
              <option value="month">Theo tháng</option>
              <option value="year">Theo năm</option>
            </select>
            <button (click)="loadAnalysis()" style="padding: 0.4rem 0.9rem; background: #2563eb; color: white; border: none; border-radius: 6px; cursor: pointer;">
              Phân tích
            </button>
          </div>
        </div>

        <div *ngIf="analysis() as ana" style="display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 1rem;">
          <div style="padding: 0.75rem; background: #f8fafc; border-radius: 8px;">
            <div style="font-size: 0.8rem; color: #64748b;">Biến động Thu so với kỳ trước</div>
            <div style="font-size: 1.15rem; font-weight: 700; margin-top: 0.25rem;">
              {{ formatChange(ana.change?.income) }}
            </div>
          </div>
          <div style="padding: 0.75rem; background: #f8fafc; border-radius: 8px;">
            <div style="font-size: 0.8rem; color: #64748b;">Biến động Chi so với kỳ trước</div>
            <div style="font-size: 1.15rem; font-weight: 700; margin-top: 0.25rem;">
              {{ formatChange(ana.change?.expense) }}
            </div>
          </div>
          <div style="padding: 0.75rem; background: #f8fafc; border-radius: 8px;">
            <div style="font-size: 0.8rem; color: #64748b;">Biến động Chênh lệch</div>
            <div style="font-size: 1.15rem; font-weight: 700; margin-top: 0.25rem;">
              {{ formatChange(ana.change?.net) }}
            </div>
          </div>
        </div>
      </div>

      <!-- Xuất báo cáo Excel & PDF (F-04) -->
      <div style="background: white; padding: 1.25rem; border-radius: 10px; box-shadow: 0 1px 3px rgba(0,0,0,0.08); overflow-x: auto;">
        <h3 style="margin: 0 0 1rem; font-size: 1.05rem;">📥 Xuất báo cáo tài chính (Excel / PDF)</h3>

        <div style="display: flex; gap: 0.75rem; flex-wrap: wrap; align-items: center; margin-bottom: 1rem;">
          <select [(ngModel)]="exportFormat" style="padding: 0.4rem 0.6rem; border: 1px solid #cbd5e1; border-radius: 6px;">
            <option value="xlsx">Excel (.xlsx)</option>
            <option value="pdf">PDF (.pdf)</option>
          </select>
          <input type="date" [(ngModel)]="exportFrom" style="padding: 0.4rem 0.6rem; border: 1px solid #cbd5e1; border-radius: 6px;" />
          <span>đến</span>
          <input type="date" [(ngModel)]="exportTo" style="padding: 0.4rem 0.6rem; border: 1px solid #cbd5e1; border-radius: 6px;" />
          <button (click)="triggerExport()" [disabled]="exportSubmitting()" style="padding: 0.45rem 1rem; background: #0f766e; color: white; border: none; border-radius: 6px; cursor: pointer;">
            {{ exportSubmitting() ? 'Đang gửi...' : 'Xuất báo cáo' }}
          </button>
        </div>

        <div *ngIf="exportError()" style="padding: 0.75rem; background: #fef2f2; color: #b91c1c; border-radius: 6px; margin-bottom: 1rem; font-size: 0.9rem;">
          {{ exportError() }}
        </div>

        <table *ngIf="exportJobs().length > 0" style="width: 100%; border-collapse: collapse; font-size: 0.88rem;">
          <thead>
            <tr style="border-bottom: 2px solid #e2e8f0; text-align: left;">
              <th style="padding: 0.5rem;">Định dạng</th>
              <th style="padding: 0.5rem;">Kỳ báo cáo</th>
              <th style="padding: 0.5rem;">Trạng thái</th>
              <th style="padding: 0.5rem;">Thao tác</th>
            </tr>
          </thead>
          <tbody>
            <tr *ngFor="let j of exportJobs()" style="border-bottom: 1px solid #f1f5f9;">
              <td style="padding: 0.5rem; text-transform: uppercase;">{{ j.format }}</td>
              <td style="padding: 0.5rem;">{{ j.from }} → {{ j.to }}</td>
              <td style="padding: 0.5rem;">
                <span [style.color]="j.status === 'Completed' ? '#16a34a' : (j.status === 'Failed' ? '#dc2626' : '#d97706')" style="font-weight: 600;">
                  {{ j.status }}
                </span>
                <span *ngIf="j.errorMessage" style="font-size: 0.8rem; color: #dc2626;"> ({{ j.errorMessage }})</span>
              </td>
              <td style="padding: 0.5rem;">
                <button *ngIf="j.status === 'Completed'" (click)="downloadFile(j)" style="padding: 0.3rem 0.7rem; background: #2563eb; color: white; border: none; border-radius: 4px; cursor: pointer; font-size: 0.8rem;">
                  Tải xuống
                </button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  `
})
export class ReportsDashboardComponent implements OnInit, OnDestroy {
  private reportService = inject(ReportService);
  private pollSub?: Subscription;

  Math = Math;
  months = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];
  selectedMonth = new Date().getMonth() + 1;
  selectedYear = new Date().getFullYear();

  analysisFrom = `${this.selectedYear}-01-01`;
  analysisTo = `${this.selectedYear}-12-31`;
  analysisGroupBy: 'day' | 'week' | 'month' | 'year' = 'month';

  exportFormat: 'xlsx' | 'pdf' = 'xlsx';
  exportFrom = `${this.selectedYear}-${String(this.selectedMonth).padStart(2, '0')}-01`;
  exportTo = `${this.selectedYear}-${String(this.selectedMonth).padStart(2, '0')}-28`;

  dashboard = signal<DashboardResponse | null>(null);
  dashboardLoading = signal(false);
  dashboardError = signal<string | null>(null);

  analysis = signal<AnalysisResponse | null>(null);
  exportJobs = signal<ExportJobDto[]>([]);
  exportSubmitting = signal(false);
  exportError = signal<string | null>(null);

  pieChartData: ChartData<'doughnut'> = { labels: [], datasets: [{ data: [] }] };
  pieChartOptions: ChartConfiguration<'doughnut'>['options'] = { responsive: true, maintainAspectRatio: false };

  barChartData: ChartData<'bar'> = { labels: [], datasets: [] };
  barChartOptions: ChartConfiguration<'bar'>['options'] = { responsive: true, maintainAspectRatio: false };

  ngOnInit(): void {
    this.loadDashboard();
    this.loadAnalysis();
    this.loadExports();
  }

  ngOnDestroy(): void {
    this.pollSub?.unsubscribe();
  }

  onPeriodChange(): void {
    this.loadDashboard();
  }

  loadDashboard(): void {
    this.dashboardLoading.set(true);
    this.dashboardError.set(null);
    this.reportService.getDashboard(this.selectedMonth, this.selectedYear).subscribe({
      next: res => {
        this.dashboard.set(res);
        this.dashboardLoading.set(false);
        this.pieChartData = {
          labels: res.byCategory.map(c => c.categoryName),
          datasets: [{
            data: res.byCategory.map(c => c.amount),
            backgroundColor: ['#3b82f6', '#10b981', '#f59e0b', '#ef4444', '#8b5cf6', '#ec4899', '#64748b']
          }]
        };
      },
      error: () => {
        this.dashboardError.set('Không thể tải dữ liệu Dashboard. Vui lòng thử lại.');
        this.dashboardLoading.set(false);
      }
    });
  }

  loadAnalysis(): void {
    this.reportService.getAnalysis(this.analysisFrom, this.analysisTo, this.analysisGroupBy, true).subscribe({
      next: res => {
        this.analysis.set(res);
        this.barChartData = {
          labels: res.points.map(p => p.period),
          datasets: [
            { label: 'Thu nhập', data: res.points.map(p => p.income), backgroundColor: '#16a34a' },
            { label: 'Chi tiêu', data: res.points.map(p => p.expense), backgroundColor: '#dc2626' }
          ]
        };
      },
      error: () => {}
    });
  }

  loadExports(): void {
    this.reportService.listExports().subscribe({
      next: jobs => this.exportJobs.set(jobs),
      error: () => {}
    });
  }

  // F-03: change = null -> "—", tuyệt đối không NaN / Infinity
  formatChange(value: number | null | undefined): string {
    if (value === null || value === undefined || Number.isNaN(value) || !Number.isFinite(value)) {
      return '—';
    }
    const sign = value > 0 ? '+' : '';
    return `${sign}${value.toFixed(1)}%`;
  }

  triggerExport(): void {
    this.exportSubmitting.set(true);
    this.exportError.set(null);
    this.reportService.createExport({
      format: this.exportFormat,
      from: this.exportFrom,
      to: this.exportTo
    }).subscribe({
      next: job => {
        this.exportSubmitting.set(false);
        this.exportJobs.update(list => [job, ...list]);
        this.pollJobStatus(job.jobId);
      },
      error: err => {
        this.exportSubmitting.set(false);
        if (err?.status === 429 || err?.error?.code === 'EXPORT_LIMIT') {
          this.exportError.set('Bạn đã đạt giới hạn tối đa 3 tác vụ xuất báo cáo đang xử lý. Vui lòng chờ hoàn tất.');
        } else {
          this.exportError.set(err?.error?.message || 'Không thể tạo tác vụ xuất báo cáo.');
        }
      }
    });
  }

  pollJobStatus(jobId: string): void {
    this.pollSub?.unsubscribe();
    this.pollSub = interval(1000).pipe(
      switchMap(() => this.reportService.getExport(jobId)),
      takeWhile(j => j.status === 'Queued' || j.status === 'Processing', true)
    ).subscribe({
      next: updated => {
        this.exportJobs.update(list => list.map(item => item.jobId === jobId ? updated : item));
      }
    });
  }

  downloadFile(job: ExportJobDto): void {
    this.reportService.downloadExportFile(job.jobId).subscribe(blob => {
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `smartwallet-report-${job.jobId}.${job.format}`;
      a.click();
      URL.revokeObjectURL(url);
    });
  }
}

