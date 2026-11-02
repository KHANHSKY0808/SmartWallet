import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterModule } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';

@Component({
  selector: 'app-confirm-email',
  standalone: true,
  imports: [CommonModule, RouterModule],
  template: `
    <div class="auth-container">
      <div class="auth-card">
        <h2>Xác nhận kích hoạt tài khoản</h2>

        <div *ngIf="isLoading()" class="status-box">
          <div class="spinner"></div>
          <p>Đang xác thực mã kích hoạt của bạn...</p>
        </div>

        <div *ngIf="isSuccess()" class="alert alert-success">
          <h3>✅ Kích hoạt thành công!</h3>
          <p>{{ message() }}</p>
          <a routerLink="/login" class="btn btn-primary">Đăng nhập ngay</a>
        </div>

        <div *ngIf="isError()" class="alert alert-danger">
          <h3>❌ Kích hoạt không thành công</h3>
          <p>{{ message() }}</p>
          <div style="margin-top: 1rem; display: flex; gap: 0.5rem; justify-content: center;">
            <a routerLink="/login" class="btn btn-primary">Đăng nhập ngay</a>
            <a routerLink="/register" class="btn btn-secondary">Đăng ký lại</a>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .auth-container {
      display: flex;
      justify-content: center;
      align-items: center;
      min-height: 80vh;
      padding: 1rem;
    }
    .auth-card {
      background: #ffffff;
      border-radius: 8px;
      box-shadow: 0 4px 12px rgba(0,0,0,0.1);
      padding: 2.5rem;
      width: 100%;
      max-width: 440px;
      text-align: center;
    }
    h2 {
      margin: 0 0 1.5rem 0;
      color: #1e293b;
      font-size: 1.4rem;
      font-weight: 700;
    }
    .status-box {
      padding: 2rem 0;
      color: #64748b;
    }
    .spinner {
      width: 40px;
      height: 40px;
      margin: 0 auto 1rem;
      border: 4px solid #e2e8f0;
      border-top-color: #2563eb;
      border-radius: 50%;
      animation: spin 0.8s linear infinite;
    }
    @keyframes spin {
      to { transform: rotate(360deg); }
    }
    .alert {
      padding: 1.25rem;
      border-radius: 6px;
      text-align: center;
    }
    .alert h3 {
      margin: 0 0 0.5rem 0;
      font-size: 1.1rem;
    }
    .alert-success {
      background-color: #dcfce7;
      color: #15803d;
      border: 1px solid #86efac;
    }
    .alert-danger {
      background-color: #fee2e2;
      color: #b91c1c;
      border: 1px solid #f87171;
    }
    .btn {
      display: inline-block;
      padding: 0.65rem 1.25rem;
      border-radius: 6px;
      text-decoration: none;
      font-weight: 600;
    }
    .btn-primary {
      background-color: #2563eb;
      color: #ffffff;
    }
    .btn-secondary {
      background-color: #64748b;
      color: #ffffff;
    }
  `]
})
export class ConfirmEmailComponent implements OnInit {
  isLoading = signal(true);
  isSuccess = signal(false);
  isError = signal(false);
  message = signal('');

  constructor(
    private route: ActivatedRoute,
    private authService: AuthService
  ) {}

  ngOnInit(): void {
    const token = this.route.snapshot.queryParamMap.get('token');
    if (!token) {
      this.isLoading.set(false);
      this.isError.set(true);
      this.message.set('Không tìm thấy mã xác nhận trong liên kết.');
      return;
    }

    this.authService.confirmEmail(token).subscribe({
      next: res => {
        this.isLoading.set(false);
        this.isSuccess.set(true);
        this.message.set(res.message || 'Tài khoản của bạn đã được kích hoạt thành công.');
      },
      error: err => {
        this.isLoading.set(false);
        this.isError.set(true);
        this.message.set(err.error?.message || 'Mã xác nhận không hợp lệ hoặc đã hết hạn sử dụng.');
      }
    });
  }
}

