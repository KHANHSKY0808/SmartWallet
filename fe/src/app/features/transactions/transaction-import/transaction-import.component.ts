import { Component, OnDestroy, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { TransactionService, ImportJobStatusDto } from '../../../core/services/transaction.service';

@Component({
  selector: 'app-transaction-import',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  template: `
    <div style="max-width: 960px; margin: 0 auto; padding: 1rem; box-sizing: border-box;">
      <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 1.25rem; flex-wrap: wrap; gap: 0.75rem;">
        <div>
          <h2 style="margin: 0; font-size: 1.45rem; color: #1e293b;">📥 Nhập giao dịch từ CSV / Excel</h2>
          <p style="margin: 0.25rem 0 0; color: #64748b; font-size: 0.9rem;">Hỗ trợ tệp .csv (UTF-8, dấu , hoặc ;) và .xlsx, tối đa 5 MB và 10.000 dòng</p>
        </div>
        <a routerLink="/transactions" style="color: #2563eb; text-decoration: none; font-weight: 500;">← Quay lại danh sách</a>
      </div>

      <!-- F-01: Chọn file & xem trước 20 dòng -->
      <div style="background: white; border: 1px solid #e2e8f0; border-radius: 10px; padding: 1.25rem; margin-bottom: 1.25rem;">
        <label style="display: block; font-weight: 600; margin-bottom: 0.5rem;">1. Chọn tệp CSV hoặc Excel (.xlsx)</label>
        <input type="file" accept=".csv,.xlsx" (change)="onFileSelected($event)" />

        <div *ngIf="clientError()" class="client-error" style="margin-top: 0.75rem; padding: 0.65rem; background: #fef2f2; color: #b91c1c; border-radius: 6px; font-size: 0.9rem;">
          {{ clientError() }}
        </div>

        <div *ngIf="headers().length > 0" style="margin-top: 1.25rem;">
          <h3 style="margin: 0 0 0.75rem; font-size: 1.05rem;">2. Ánh xạ cột (F-02)</h3>
          <div style="display: grid; grid-template-columns: repeat(auto-fit, minmax(170px, 1fr)); gap: 0.75rem;">
            <div>
              <label style="display: block; font-size: 0.8rem; color: #475569;">Ngày giao dịch *</label>
              <select [(ngModel)]="mapping.date" style="width: 100%; padding: 0.4rem; border: 1px solid #cbd5e1; border-radius: 6px;">
                <option *ngFor="let h of headers()" [value]="h">{{ h }}</option>
              </select>
            </div>
            <div>
              <label style="display: block; font-size: 0.8rem; color: #475569;">Số tiền *</label>
              <select [(ngModel)]="mapping.amount" style="width: 100%; padding: 0.4rem; border: 1px solid #cbd5e1; border-radius: 6px;">
                <option *ngFor="let h of headers()" [value]="h">{{ h }}</option>
              </select>
            </div>
            <div>
              <label style="display: block; font-size: 0.8rem; color: #475569;">Loại (Chi/Thu) *</label>
              <select [(ngModel)]="mapping.type" style="width: 100%; padding: 0.4rem; border: 1px solid #cbd5e1; border-radius: 6px;">
                <option *ngFor="let h of headers()" [value]="h">{{ h }}</option>
              </select>
            </div>
            <div>
              <label style="display: block; font-size: 0.8rem; color: #475569;">Danh mục *</label>
              <select [(ngModel)]="mapping.category" style="width: 100%; padding: 0.4rem; border: 1px solid #cbd5e1; border-radius: 6px;">
                <option *ngFor="let h of headers()" [value]="h">{{ h }}</option>
              </select>
            </div>
            <div>
              <label style="display: block; font-size: 0.8rem; color: #475569;">Ví *</label>
              <select [(ngModel)]="mapping.wallet" style="width: 100%; padding: 0.4rem; border: 1px solid #cbd5e1; border-radius: 6px;">
                <option *ngFor="let h of headers()" [value]="h">{{ h }}</option>
              </select>
            </div>
            <div>
              <label style="display: block; font-size: 0.8rem; color: #475569;">Ghi chú</label>
              <select [(ngModel)]="mapping.note" style="width: 100%; padding: 0.4rem; border: 1px solid #cbd5e1; border-radius: 6px;">
                <option value="">-- Bỏ qua --</option>
                <option *ngFor="let h of headers()" [value]="h">{{ h }}</option>
              </select>
            </div>
          </div>
        </div>

        <div *ngIf="previewRows().length > 0" style="margin-top: 1.25rem; overflow-x: auto;">
          <div style="font-weight: 600; font-size: 0.95rem; margin-bottom: 0.5rem;">Xem trước ({{ previewRows().length }} dòng đầu)</div>
          <table class="preview-table" style="width: 100%; border-collapse: collapse; font-size: 0.85rem;">
            <thead>
              <tr style="background: #f8fafc; border-bottom: 1px solid #e2e8f0;">
                <th *ngFor="let h of headers()" style="padding: 0.45rem; text-align: left;">{{ h }}</th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let r of previewRows()" style="border-bottom: 1px solid #f1f5f9;">
                <td *ngFor="let c of r" style="padding: 0.4rem;">{{ c }}</td>
              </tr>
            </tbody>
          </table>
        </div>

        <div style="margin-top: 1rem;">
          <button
            class="btn-submit-import"
            (click)="submitImport()"
            [disabled]="!selectedFile() || uploading()"
            style="padding: 0.6rem 1.25rem; background: #2563eb; color: white; border: none; border-radius: 6px; font-weight: 600; cursor: pointer;">
            {{ uploading() ? 'Đang tải lên...' : 'Bắt đầu nhập dữ liệu' }}
          </button>
        </div>
      </div>

      <!-- F-03: Tiến độ & kết quả -->
      <div *ngIf="job()" class="import-result" style="background: white; border: 1px solid #e2e8f0; border-radius: 10px; padding: 1.25rem;">
        <div style="display: flex; justify-content: space-between; align-items: center; flex-wrap: wrap; gap: 0.5rem;">
          <h3 style="margin: 0;">3. Kết quả nhập dữ liệu ({{ job()!.status }})</h3>
          <button
            *ngIf="job()!.failed > 0"
            class="btn-download-errors"
            (click)="downloadErrors()"
            style="padding: 0.45rem 0.9rem; background: #dc2626; color: white; border: none; border-radius: 6px; cursor: pointer;">
            ⬇️ Tải file lỗi (errors.csv)
          </button>
        </div>

        <div style="margin: 0.75rem 0; background: #e2e8f0; border-radius: 999px; height: 10px; overflow: hidden;">
          <div class="progress-bar-fill" [style.width.%]="progressPercent()" style="height: 100%; background: #16a34a; transition: width 0.3s;"></div>
        </div>

        <div style="display: flex; gap: 1.5rem; flex-wrap: wrap; font-size: 0.92rem; margin-bottom: 0.75rem;">
          <span>Tổng dòng: <strong>{{ job()!.totalRows }}</strong></span>
          <span style="color: #16a34a;">Thành công: <strong>{{ job()!.succeeded }}</strong></span>
          <span style="color: #dc2626;">Thất bại: <strong>{{ job()!.failed }}</strong></span>
        </div>

        <table *ngIf="job()!.errors.length > 0" class="errors-table" style="width: 100%; border-collapse: collapse; font-size: 0.88rem;">
          <thead>
            <tr style="background: #fef2f2; border-bottom: 1px solid #fecaca;">
              <th style="padding: 0.5rem; text-align: left; width: 90px;">Dòng</th>
              <th style="padding: 0.5rem; text-align: left;">Lý do</th>
            </tr>
          </thead>
          <tbody>
            <tr *ngFor="let err of job()!.errors" style="border-bottom: 1px solid #f1f5f9;">
              <td style="padding: 0.45rem; font-weight: 600;">{{ err.row }}</td>
              <td style="padding: 0.45rem; color: #991b1b;">{{ err.reason }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  `
})
export class TransactionImportComponent implements OnDestroy {
  private txService = inject(TransactionService);
  private pollTimer: ReturnType<typeof setInterval> | null = null;
  readonly maxFileSize = 5 * 1024 * 1024;

  selectedFile = signal<File | null>(null);
  clientError = signal<string | null>(null);
  headers = signal<string[]>([]);
  previewRows = signal<string[][]>([]);
  uploading = signal(false);
  job = signal<ImportJobStatusDto | null>(null);

  mapping: { date: string; amount: string; type: string; category: string; wallet: string; note: string } = {
    date: '',
    amount: '',
    type: '',
    category: '',
    wallet: '',
    note: ''
  };

  ngOnDestroy(): void {
    this.stopPolling();
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    this.handleFile(file);
  }

  handleFile(file: File | null): void {
    this.clientError.set(null);
    this.headers.set([]);
    this.previewRows.set([]);
    this.selectedFile.set(null);

    if (!file) return;

    if (file.size > this.maxFileSize) {
      this.clientError.set('Tệp vượt quá giới hạn tối đa 5 MB.');
      return;
    }

    const lower = file.name.toLowerCase();
    if (!lower.endsWith('.csv') && !lower.endsWith('.xlsx')) {
      this.clientError.set('Chỉ hỗ trợ tệp định dạng .csv hoặc .xlsx.');
      return;
    }

    this.selectedFile.set(file);

    if (lower.endsWith('.csv')) {
      const reader = new FileReader();
      reader.onload = () => {
        this.parseCsvPreview(String(reader.result ?? ''));
      };
      reader.readAsText(file, 'utf-8');
    }
  }

  parseCsvPreview(rawText: string): void {
    const clean = rawText.replace(/^\uFEFF/, '');
    const lines = clean.split(/\r?\n/).filter(l => l.trim().length > 0);
    if (lines.length === 0) return;

    const sep = (lines[0].split(';').length > lines[0].split(',').length) ? ';' : ',';
    const parsedHeaders = lines[0].split(sep).map(h => h.trim());
    this.headers.set(parsedHeaders);
    this.suggestMapping(parsedHeaders);

    const rows = lines.slice(1, 21).map(line => line.split(sep).map(c => c.trim()));
    this.previewRows.set(rows);
  }

  suggestMapping(hdrs: string[]): void {
    const norm = (s: string) => s.normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/[đĐ]/g, 'd').toLowerCase().replace(/[^a-z0-9]/g, '');
    const find = (keys: string[]) => hdrs.find(h => keys.includes(norm(h))) ?? '';
    this.mapping = {
      date: find(['ngay', 'date', 'transactiondate']),
      amount: find(['sotien', 'amount']),
      type: find(['loai', 'type']),
      category: find(['danhmuc', 'category']),
      wallet: find(['vi', 'wallet']),
      note: find(['ghichu', 'note', 'description'])
    };
  }

  submitImport(): void {
    const file = this.selectedFile();
    if (!file) return;

    this.uploading.set(true);
    this.clientError.set(null);

    const mappingPayload = this.headers().length > 0 ? this.mapping : undefined;
    this.txService.uploadImport(file, mappingPayload).subscribe({
      next: res => {
        this.uploading.set(false);
        this.refreshStatus(res.jobId);
        this.stopPolling();
        this.pollTimer = setInterval(() => this.refreshStatus(res.jobId), 1500);
      },
      error: err => {
        this.uploading.set(false);
        this.clientError.set(err?.error?.message ?? 'Không thể tải tệp lên máy chủ.');
      }
    });
  }

  refreshStatus(jobId: string): void {
    this.txService.getImportStatus(jobId).subscribe({
      next: status => {
        this.job.set(status);
        if (status.status === 'Completed' || status.status === 'Failed') {
          this.stopPolling();
        }
      }
    });
  }

  progressPercent(): number {
    const j = this.job();
    if (!j || j.totalRows <= 0) return 0;
    return Math.min(100, Math.round(((j.succeeded + j.failed) / j.totalRows) * 100));
  }

  downloadErrors(): void {
    const j = this.job();
    if (!j) return;
    this.txService.downloadImportErrors(j.jobId).subscribe(blob => {
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `import-errors-${j.jobId}.csv`;
      a.click();
      URL.revokeObjectURL(url);
    });
  }

  private stopPolling(): void {
    if (this.pollTimer !== null) {
      clearInterval(this.pollTimer);
      this.pollTimer = null;
    }
  }
}
