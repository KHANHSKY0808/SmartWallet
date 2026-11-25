import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AdminService, AuditLogItem } from '../../../core/services/admin.service';

@Component({
  selector: 'app-admin-audit',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div style="display: flex; flex-direction: column; gap: 1.25rem; max-width: 100%;">
      <div>
        <h2 style="margin: 0; font-size: 1.4rem; color: #0f172a;">Nhật ký kiểm toán hệ thống (Audit Log)</h2>
        <p style="margin: 0.25rem 0 0; color: #64748b; font-size: 0.9rem;">Dữ liệu bất biến ghi nhận toàn bộ sự kiện nghiệp vụ và thao tác quản trị (đã che dữ liệu nhạy cảm).</p>
      </div>

      <!-- Bộ lọc (F-04) -->
      <div style="display: flex; flex-wrap: wrap; gap: 0.75rem; background: white; padding: 1rem; border-radius: 10px; box-shadow: 0 1px 3px rgba(0,0,0,0.08);">
        <input type="text" [(ngModel)]="userIdFilter" placeholder="User ID (GUID)..."
               style="flex: 1 1 180px; min-width: 0; padding: 0.45rem 0.65rem; border: 1px solid #cbd5e1; border-radius: 6px;" />
        <input type="text" [(ngModel)]="actionFilter" placeholder="Action (vd: Created, UserLocked)..."
               style="flex: 1 1 160px; min-width: 0; padding: 0.45rem 0.65rem; border: 1px solid #cbd5e1; border-radius: 6px;" />
        <input type="text" [(ngModel)]="serviceFilter" placeholder="Service (vd: TransactionService)..."
               style="flex: 1 1 160px; min-width: 0; padding: 0.45rem 0.65rem; border: 1px solid #cbd5e1; border-radius: 6px;" />
        <input type="date" [(ngModel)]="fromDate" style="padding: 0.45rem; border: 1px solid #cbd5e1; border-radius: 6px;" />
        <input type="date" [(ngModel)]="toDate" style="padding: 0.45rem; border: 1px solid #cbd5e1; border-radius: 6px;" />
        <button (click)="applyFilter()" style="padding: 0.45rem 1rem; background: #2563eb; color: white; border: none; border-radius: 6px; font-weight: 600; cursor: pointer;">
          Lọc
        </button>
      </div>

      <div *ngIf="errorMsg()" style="padding: 0.75rem 1rem; background: #fee2e2; color: #991b1b; border-radius: 8px;">
        ⚠️ {{ errorMsg() }}
      </div>

      <!-- Bảng Audit -->
      <div style="background: white; border-radius: 10px; box-shadow: 0 1px 3px rgba(0,0,0,0.08); overflow-x: auto;">
        <table style="width: 100%; border-collapse: collapse; min-width: 720px;">
          <thead>
            <tr style="background: #f8fafc; border-bottom: 1px solid #e2e8f0; text-align: left;">
              <th style="padding: 0.7rem 0.9rem; font-size: 0.85rem;">Thời gian</th>
              <th style="padding: 0.7rem 0.9rem; font-size: 0.85rem;">Service</th>
              <th style="padding: 0.7rem 0.9rem; font-size: 0.85rem;">Action</th>
              <th style="padding: 0.7rem 0.9rem; font-size: 0.85rem;">User / Entity</th>
              <th style="padding: 0.7rem 0.9rem; font-size: 0.85rem;">Chi tiết (Masked)</th>
            </tr>
          </thead>
          <tbody>
            <tr *ngFor="let log of logs()" style="border-bottom: 1px solid #f1f5f9;">
              <td style="padding: 0.7rem 0.9rem; font-size: 0.82rem; white-space: nowrap;">{{ log.timestamp | date:'HH:mm:ss dd/MM/yyyy' }}</td>
              <td style="padding: 0.7rem 0.9rem; font-size: 0.85rem; font-weight: 600;">{{ log.serviceName }}</td>
              <td style="padding: 0.7rem 0.9rem; font-size: 0.85rem; color: #1d4ed8;">{{ log.action }}</td>
              <td style="padding: 0.7rem 0.9rem; font-size: 0.8rem; color: #475569;">
                <div>User: {{ log.userId || '—' }}</div>
                <div>Entity: {{ log.entityId || '—' }}</div>
              </td>
              <td style="padding: 0.7rem 0.9rem; font-size: 0.78rem; color: #334155; max-width: 300px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;">
                {{ log.newValueJson || '—' }}
              </td>
            </tr>
            <tr *ngIf="logs().length === 0">
              <td colspan="5" style="padding: 2rem; text-align: center; color: #94a3b8;">Không có bản ghi kiểm toán phù hợp.</td>
            </tr>
          </tbody>
        </table>
      </div>

      <!-- Phân trang -->
      <div style="display: flex; justify-content: space-between; align-items: center; flex-wrap: wrap; gap: 0.5rem;">
        <span style="font-size: 0.85rem; color: #64748b;">Tổng số: {{ totalItems() }} bản ghi | Trang {{ page }}</span>
        <div style="display: flex; gap: 0.5rem;">
          <button (click)="prevPage()" [disabled]="page <= 1" style="padding: 0.4rem 0.8rem; border: 1px solid #cbd5e1; background: white; border-radius: 6px; cursor: pointer;">Trước</button>
          <button (click)="nextPage()" [disabled]="page * pageSize >= totalItems()" style="padding: 0.4rem 0.8rem; border: 1px solid #cbd5e1; background: white; border-radius: 6px; cursor: pointer;">Sau</button>
        </div>
      </div>
    </div>
  `
})
export class AdminAuditComponent implements OnInit {
  private adminService = inject(AdminService);

  logs = signal<AuditLogItem[]>([]);
  totalItems = signal(0);
  errorMsg = signal('');

  userIdFilter = '';
  actionFilter = '';
  serviceFilter = '';
  fromDate = '';
  toDate = '';
  page = 1;
  pageSize = 20;

  ngOnInit(): void {
    this.loadLogs();
  }

  applyFilter(): void {
    this.page = 1;
    this.loadLogs();
  }

  prevPage(): void {
    if (this.page > 1) {
      this.page--;
      this.loadLogs();
    }
  }

  nextPage(): void {
    if (this.page * this.pageSize < this.totalItems()) {
      this.page++;
      this.loadLogs();
    }
  }

  loadLogs(): void {
    this.errorMsg.set('');
    this.adminService.getAuditLogs({
      userId: this.userIdFilter || undefined,
      action: this.actionFilter || undefined,
      serviceName: this.serviceFilter || undefined,
      from: this.fromDate || undefined,
      to: this.toDate || undefined,
      page: this.page,
      pageSize: this.pageSize
    }).subscribe({
      next: res => {
        this.logs.set(res?.items || []);
        this.totalItems.set(res?.totalItems ?? res?.totalCount ?? 0);
      },
      error: err => this.errorMsg.set(err.error?.message || 'Không thể tải nhật ký kiểm toán.')
    });
  }
}

