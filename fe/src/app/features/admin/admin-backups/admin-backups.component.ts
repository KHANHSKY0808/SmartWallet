import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AdminService, BackupManifestItem } from '../../../core/services/admin.service';

@Component({
  selector: 'app-admin-backups',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div style="max-width: 100%; overflow-x: auto;">
      <div style="display: flex; justify-content: space-between; align-items: center; flex-wrap: wrap; gap: 1rem; margin-bottom: 1.25rem;">
        <div>
          <h2 style="margin: 0; font-size: 1.5rem; color: #1e293b;">💾 Sao lưu & Khôi phục Dữ liệu</h2>
          <p style="margin: 0.25rem 0 0; color: #64748b; font-size: 0.9rem;">Lịch tự động 01:00 giờ VN hằng ngày (VACUUM INTO), lưu giữ 14 bản nén .zip mới nhất</p>
        </div>
        <button
          class="btn-backup-now"
          (click)="triggerBackup()"
          [disabled]="triggering()"
          style="padding: 0.55rem 1.15rem; background: #1e3a8a; color: white; border: none; border-radius: 6px; font-weight: 600; cursor: pointer;">
          {{ triggering() ? 'Đang khởi tạo...' : 'Sao lưu ngay' }}
        </button>
      </div>

      <div *ngIf="message()" style="padding: 0.75rem 1rem; background: #eff6ff; color: #1d4ed8; border-radius: 8px; margin-bottom: 1rem;">
        {{ message() }}
      </div>

      <div style="background: white; border-radius: 10px; box-shadow: 0 1px 3px rgba(0,0,0,0.08); overflow-x: auto;">
        <table style="width: 100%; border-collapse: collapse; font-size: 0.9rem;">
          <thead>
            <tr style="background: #f8fafc; border-bottom: 2px solid #e2e8f0; text-align: left;">
              <th style="padding: 0.75rem 1rem;">Mã bản sao lưu</th>
              <th style="padding: 0.75rem 1rem;">Trạng thái</th>
              <th style="padding: 0.75rem 1rem;">Bắt đầu</th>
              <th style="padding: 0.75rem 1rem;">Chi tiết từng service</th>
            </tr>
          </thead>
          <tbody>
            <tr *ngFor="let b of backups()" style="border-bottom: 1px solid #f1f5f9; vertical-align: top;">
              <td style="padding: 0.75rem 1rem; font-weight: 600;">{{ b.backupId }}</td>
              <td style="padding: 0.75rem 1rem;">
                <span style="padding: 0.2rem 0.6rem; border-radius: 999px; font-size: 0.8rem; font-weight: 600;"
                      [style.background]="b.status === 'Completed' ? '#dcfce7' : (b.status === 'PartiallyFailed' ? '#fef3c7' : '#fee2e2')"
                      [style.color]="b.status === 'Completed' ? '#166534' : (b.status === 'PartiallyFailed' ? '#92400e' : '#991b1b')">
                  {{ b.status }}
                </span>
              </td>
              <td style="padding: 0.75rem 1rem; color: #64748b;">{{ b.startedAt | date:'HH:mm:ss dd/MM/yyyy' }}</td>
              <td style="padding: 0.75rem 1rem;">
                <div style="display: flex; flex-wrap: wrap; gap: 0.35rem;">
                  <span *ngFor="let f of b.files" style="background: #dcfce7; color: #166534; padding: 0.15rem 0.45rem; border-radius: 4px; font-size: 0.78rem;">
                    ✓ {{ f.service }} ({{ f.sizeBytes }} B)
                  </span>
                  <span *ngFor="let err of b.failedServices" style="background: #fee2e2; color: #991b1b; padding: 0.15rem 0.45rem; border-radius: 4px; font-size: 0.78rem;">
                    ✗ {{ err.service }}: {{ err.error }}
                  </span>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  `
})
export class AdminBackupsComponent implements OnInit {
  private adminService = inject(AdminService);

  backups = signal<BackupManifestItem[]>([]);
  triggering = signal(false);
  message = signal<string | null>(null);

  ngOnInit(): void {
    this.loadHistory();
  }

  loadHistory(): void {
    this.adminService.getBackups().subscribe({
      next: data => this.backups.set(data),
      error: () => this.message.set('Không thể tải lịch sử sao lưu (yêu cầu quyền backups.write).')
    });
  }

  triggerBackup(): void {
    this.triggering.set(true);
    this.message.set(null);
    this.adminService.triggerBackup().subscribe({
      next: res => {
        this.triggering.set(false);
        this.message.set(`Đã khởi tạo bản sao lưu ${res.backupId}`);
        this.loadHistory();
      },
      error: () => {
        this.triggering.set(false);
        this.message.set('Lỗi khi kích hoạt sao lưu.');
      }
    });
  }
}

