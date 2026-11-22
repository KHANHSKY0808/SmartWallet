import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { WalletService } from '../../../core/services/wallet.service';
import { WalletDto, TotalAssetsResponse } from '../../../core/models/wallet.models';

@Component({
  selector: 'app-wallet-list',
  standalone: true,
  imports: [CommonModule, RouterModule],
  template: `
    <div class="wallet-list-container" style="max-width: 100%; box-sizing: border-box; overflow-x: hidden; padding: 1rem;">
      <!-- Header & Actions -->
      <div class="header-section" style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 1.5rem; flex-wrap: wrap; gap: 1rem;">
        <div>
          <h2 style="margin: 0; font-size: 1.5rem; color: #1e293b;">Danh sách ví của tôi</h2>
          <p style="margin: 0.25rem 0 0 0; color: #64748b; font-size: 0.9rem;">Quản lý các tài khoản tiền mặt, ngân hàng và thẻ</p>
        </div>
        <div style="display: flex; gap: 0.75rem; flex-wrap: wrap;">
          <a routerLink="/wallets/transfer" class="btn btn-primary" style="text-decoration: none;">Chuyển tiền</a>
          <a routerLink="/wallets/create" class="btn btn-success" style="text-decoration: none;">+ Thêm ví mới</a>
        </div>
      </div>

      <!-- F-04: Total Assets Card -->
      <div *ngIf="totalAssets()" class="total-assets-card" style="background: linear-gradient(135deg, #1e3a8a, #2563eb); color: white; border-radius: 8px; padding: 1.5rem; margin-bottom: 1.5rem; box-shadow: 0 4px 10px rgba(37,99,235,0.2);">
        <div style="font-size: 0.9rem; opacity: 0.9; text-transform: uppercase; letter-spacing: 0.05em;">Tổng tài sản quy đổi (VND)</div>
        <div class="total-assets" style="font-size: 2rem; font-weight: 700; margin: 0.5rem 0;">
          {{ totalAssets()?.totalAssetsVnd | number }} ₫
        </div>

        <!-- F-04: Excluded Wallets Warning -->
        <div *ngIf="totalAssets()?.excludedWallets?.length" class="alert-warning excluded-warning" style="background: rgba(254, 240, 138, 0.25); border: 1px solid rgba(250, 204, 21, 0.4); border-radius: 6px; padding: 0.75rem 1rem; margin-top: 1rem; font-size: 0.85rem; color: #fef08a;">
          ⚠️ <strong>Lưu ý:</strong> Có {{ totalAssets()?.excludedWallets?.length }} ví ngoại tệ chưa được quy đổi do chưa cập nhật tỷ giá:
          <ul style="margin: 0.25rem 0 0 1.25rem; padding: 0;">
            <li *ngFor="let ex of totalAssets()?.excludedWallets">
              {{ ex.name }}: {{ ex.balance | number }} {{ ex.currency }}
            </li>
          </ul>
        </div>
      </div>

      <!-- Loading / Error states -->
      <div *ngIf="isLoading()" style="text-align: center; padding: 2rem; color: #64748b;">
        Đang tải danh sách ví...
      </div>

      <div *ngIf="errorMessage()" class="alert alert-danger" style="background: #fef2f2; color: #b91c1c; padding: 0.75rem 1rem; border-radius: 6px; margin-bottom: 1rem;">
        {{ errorMessage() }}
      </div>

      <!-- F-04: Empty State -->
      <div *ngIf="!isLoading() && wallets().length === 0" class="empty-state" style="text-align: center; padding: 3rem 1rem; background: white; border-radius: 8px; border: 1px dashed #cbd5e1; margin-top: 1rem;">
        <div style="font-size: 3rem; margin-bottom: 0.5rem;">👛</div>
        <h3 style="margin: 0 0 0.5rem 0; color: #334155;">Chưa có ví nào</h3>
        <p style="color: #64748b; margin-bottom: 1.5rem; font-size: 0.9rem;">Hãy tạo ví đầu tiên của bạn để bắt đầu theo dõi thu chi.</p>
        <a routerLink="/wallets/create" class="btn btn-primary" style="text-decoration: none;">+ Tạo ví ngay</a>
      </div>

      <!-- Wallet Grid -->
      <div *ngIf="!isLoading() && wallets().length > 0" class="wallet-grid" style="display: grid; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); gap: 1rem;">
        <div *ngFor="let w of wallets()" class="wallet-card" style="background: white; border-radius: 8px; padding: 1.25rem; box-shadow: 0 2px 8px rgba(0,0,0,0.06); border: 1px solid #e2e8f0; display: flex; flex-direction: column; justify-content: space-between;">
          <div>
            <div style="display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 0.75rem;">
              <span style="font-size: 1.25rem;">
                {{ getWalletIcon(w.type) }}
              </span>
              <span [class]="getWalletTypeBadgeClass(w.type)" style="font-size: 0.75rem; padding: 0.2rem 0.5rem; border-radius: 4px; font-weight: 500;">
                {{ getWalletTypeName(w.type) }}
              </span>
            </div>
            <h4 style="margin: 0 0 0.25rem 0; font-size: 1.1rem; color: #1e293b;">{{ w.name }}</h4>
            <div style="font-size: 1.4rem; font-weight: 700; color: #2563eb; margin: 0.5rem 0;">
              {{ w.balance | number }} <span style="font-size: 0.9rem; font-weight: 500; color: #64748b;">{{ w.currency }}</span>
            </div>
          </div>
          <div style="border-top: 1px solid #f1f5f9; padding-top: 0.75rem; margin-top: 0.75rem; display: flex; justify-content: flex-end; gap: 0.5rem;">
            <button (click)="archiveWallet(w.id)" class="btn-sm btn-outline-danger" style="background: none; border: 1px solid #fca5a5; color: #dc2626; border-radius: 4px; padding: 0.3rem 0.6rem; font-size: 0.8rem; cursor: pointer;">
              Lưu trữ
            </button>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .wallet-list-container {
      width: 100%;
      max-width: 100%;
      box-sizing: border-box;
      overflow-x: hidden;
    }
    .btn {
      padding: 0.5rem 1rem;
      font-size: 0.9rem;
      font-weight: 600;
      border-radius: 6px;
      cursor: pointer;
      display: inline-block;
    }
    .btn-primary { background: #2563eb; color: white; }
    .btn-success { background: #16a34a; color: white; }
    .badge-cash { background: #dcfce7; color: #166534; }
    .badge-bank { background: #dbeafe; color: #1e40af; }
    .badge-ewallet { background: #fef3c7; color: #92400e; }
    .badge-card { background: #f3e8ff; color: #6b21a8; }
  `]
})
export class WalletListComponent implements OnInit {
  private walletService = inject(WalletService);

  wallets = signal<WalletDto[]>([]);
  totalAssets = signal<TotalAssetsResponse | null>(null);
  isLoading = signal<boolean>(false);
  errorMessage = signal<string | null>(null);

  ngOnInit(): void {
    this.loadData();
  }

  loadData(): void {
    this.isLoading.set(true);
    this.errorMessage.set(null);

    this.walletService.getWallets().subscribe({
      next: (data) => {
        this.wallets.set(data);
        this.loadTotalAssets();
      },
      error: () => {
        this.isLoading.set(false);
        this.errorMessage.set('Không thể tải danh sách ví');
      }
    });
  }

  loadTotalAssets(): void {
    this.walletService.getTotalAssets().subscribe({
      next: (res) => {
        this.totalAssets.set(res);
        this.isLoading.set(false);
      },
      error: () => {
        this.isLoading.set(false);
      }
    });
  }

  archiveWallet(id: string): void {
    if (confirm('Bạn có chắc muốn lưu trữ ví này không?')) {
      this.walletService.deleteWallet(id).subscribe({
        next: () => this.loadData(),
        error: (err) => alert(err.error?.message || 'Không thể lưu trữ ví')
      });
    }
  }

  getWalletIcon(type: number): string {
    switch (type) {
      case 1: return '💵';
      case 2: return '🏦';
      case 3: return '📱';
      case 4: return '💳';
      default: return '👛';
    }
  }

  getWalletTypeName(type: number): string {
    switch (type) {
      case 1: return 'Tiền mặt';
      case 2: return 'Ngân hàng';
      case 3: return 'Ví điện tử';
      case 4: return 'Thẻ tín dụng';
      default: return 'Khác';
    }
  }

  getWalletTypeBadgeClass(type: number): string {
    switch (type) {
      case 1: return 'badge-cash';
      case 2: return 'badge-bank';
      case 3: return 'badge-ewallet';
      case 4: return 'badge-card';
      default: return 'badge-bank';
    }
  }
}

