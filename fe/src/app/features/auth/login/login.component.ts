import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterModule],
  template: `
    <div class="auth-container">
      <div class="auth-card">
        <h2>Đăng nhập SmartWallet</h2>
        <p class="subtitle">Quản lý chi tiêu thông minh và hiệu quả</p>

        <div *ngIf="errorMessage" class="alert alert-danger">
          {{ errorMessage }}
        </div>

        <div *ngIf="lockoutMessage" class="alert alert-warning">
          <strong>⚠️ Tài khoản tạm khóa:</strong> {{ lockoutMessage }}
        </div>

        <form [formGroup]="loginForm" (ngSubmit)="onSubmit()">
          <div class="form-group">
            <label for="email">Email</label>
            <input
              id="email"
              type="email"
              formControlName="email"
              placeholder="name@example.com"
              [class.is-invalid]="f['email'].touched && f['email'].invalid"
            />
            <div *ngIf="f['email'].touched && f['email'].errors" class="invalid-feedback">
              <span *ngIf="f['email'].errors['required']">Email không được để trống</span>
              <span *ngIf="f['email'].errors['email']">Email không hợp lệ</span>
            </div>
          </div>

          <div class="form-group">
            <label for="password">Mật khẩu</label>
            <input
              id="password"
              type="password"
              formControlName="password"
              placeholder="••••••••"
              [class.is-invalid]="f['password'].touched && f['password'].invalid"
            />
            <div *ngIf="f['password'].touched && f['password'].errors" class="invalid-feedback">
              <span *ngIf="f['password'].errors['required']">Mật khẩu không được để trống</span>
            </div>
          </div>

          <div class="form-actions">
            <a routerLink="/forgot-password" class="forgot-link">Quên mật khẩu?</a>
          </div>

          <button type="submit" [disabled]="loginForm.invalid || isLoading" class="btn btn-primary">
            <span *ngIf="!isLoading">Đăng nhập</span>
            <span *ngIf="isLoading">Đang xử lý...</span>
          </button>
        </form>

        <div class="auth-footer">
          Chưa có tài khoản? <a routerLink="/register">Đăng ký ngay</a>
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
      max-width: 420px;
    }
    h2 {
      margin: 0 0 0.5rem 0;
      color: #1e293b;
      font-size: 1.5rem;
      font-weight: 700;
      text-align: center;
    }
    .subtitle {
      text-align: center;
      color: #64748b;
      margin-bottom: 1.5rem;
      font-size: 0.9rem;
    }
    .alert {
      padding: 0.75rem 1rem;
      border-radius: 6px;
      margin-bottom: 1rem;
      font-size: 0.875rem;
    }
    .alert-danger {
      background-color: #fee2e2;
      color: #b91c1c;
      border: 1px solid #f87171;
    }
    .alert-warning {
      background-color: #fef3c7;
      color: #b45309;
      border: 1px solid #fcd34d;
    }
    .form-group {
      margin-bottom: 1.25rem;
    }
    label {
      display: block;
      margin-bottom: 0.4rem;
      font-weight: 600;
      font-size: 0.875rem;
      color: #334155;
    }
    input {
      width: 100%;
      box-sizing: border-box;
      padding: 0.65rem 0.85rem;
      border: 1px solid #cbd5e1;
      border-radius: 6px;
      font-size: 0.95rem;
      transition: border-color 0.2s;
    }
    input:focus {
      outline: none;
      border-color: #2563eb;
    }
    input.is-invalid {
      border-color: #ef4444;
    }
    .invalid-feedback {
      color: #ef4444;
      font-size: 0.8rem;
      margin-top: 0.25rem;
    }
    .form-actions {
      display: flex;
      justify-content: flex-end;
      margin-bottom: 1.25rem;
    }
    .forgot-link {
      color: #2563eb;
      font-size: 0.85rem;
      text-decoration: none;
    }
    .forgot-link:hover {
      text-decoration: underline;
    }
    .btn {
      width: 100%;
      padding: 0.75rem;
      border: none;
      border-radius: 6px;
      font-size: 1rem;
      font-weight: 600;
      cursor: pointer;
      transition: background-color 0.2s;
    }
    .btn-primary {
      background-color: #2563eb;
      color: #ffffff;
    }
    .btn-primary:hover:not(:disabled) {
      background-color: #1d4ed8;
    }
    .btn:disabled {
      opacity: 0.6;
      cursor: not-allowed;
    }
    .auth-footer {
      text-align: center;
      margin-top: 1.5rem;
      font-size: 0.9rem;
      color: #64748b;
    }
    .auth-footer a {
      color: #2563eb;
      text-decoration: none;
      font-weight: 600;
    }
    .auth-footer a:hover {
      text-decoration: underline;
    }
  `]
})
export class LoginComponent {
  loginForm: FormGroup;
  isLoading = false;
  errorMessage = '';
  lockoutMessage = '';

  constructor(
    private fb: FormBuilder,
    private authService: AuthService,
    private router: Router
  ) {
    this.loginForm = this.fb.group({
      email: ['', [Validators.required, Validators.email]],
      password: ['', [Validators.required]]
    });
  }

  get f() {
    return this.loginForm.controls;
  }

  onSubmit(): void {
    if (this.loginForm.invalid) return;

    this.isLoading = true;
    this.errorMessage = '';
    this.lockoutMessage = '';

    this.authService.login(this.loginForm.value).subscribe({
      next: () => {
        this.isLoading = false;
        this.router.navigate(['/profile']);
      },
      error: err => {
        this.isLoading = false;
        if (err.status === 423) {
          this.lockoutMessage = err.error?.message || 'Tài khoản tạm thời bị khóa do nhập sai mật khẩu nhiều lần.';
        } else if (err.status === 403) {
          this.errorMessage = err.error?.message || 'Tài khoản chưa được kích hoạt.';
        } else {
          this.errorMessage = err.error?.message || 'Email hoặc mật khẩu không chính xác';
        }
      }
    });
  }
}
