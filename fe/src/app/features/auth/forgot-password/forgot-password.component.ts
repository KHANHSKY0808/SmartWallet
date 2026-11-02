import { Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';

@Component({
  selector: 'app-forgot-password',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterModule],
  template: `
    <div class="auth-container">
      <div class="auth-card">
        <h2>Quên mật khẩu</h2>
        <p class="subtitle">Nhập email của bạn để nhận liên kết khôi phục mật khẩu (hiệu lực 30 phút)</p>

        <div *ngIf="successMessage()" class="alert alert-success">
          <strong>📨 Đã gửi yêu cầu:</strong> {{ successMessage() }}
        </div>

        <div *ngIf="errorMessage()" class="alert alert-danger">
          {{ errorMessage() }}
        </div>

        <form *ngIf="!successMessage()" [formGroup]="forgotForm" (ngSubmit)="onSubmit()">
          <div class="form-group">
            <label for="email">Email tài khoản</label>
            <input
              id="email"
              type="email"
              formControlName="email"
              placeholder="name@example.com"
              [class.is-invalid]="f['email'].touched && f['email'].invalid"
            />
            <div *ngIf="f['email'].touched && f['email'].errors" class="invalid-feedback">
              <span *ngIf="f['email'].errors['required']">Email không được để trống</span>
              <span *ngIf="f['email'].errors['email']">Email không đúng định dạng</span>
            </div>
          </div>

          <button type="submit" [disabled]="forgotForm.invalid || isLoading()" class="btn btn-primary">
            <span *ngIf="!isLoading()">Gửi liên kết khôi phục</span>
            <span *ngIf="isLoading()">Đang xử lý...</span>
          </button>
        </form>

        <div class="auth-footer">
          Nhớ lại mật khẩu? <a routerLink="/login">Quay lại đăng nhập</a>
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
      line-height: 1.4;
    }
    .alert {
      padding: 0.85rem 1rem;
      border-radius: 6px;
      margin-bottom: 1.25rem;
      font-size: 0.875rem;
      line-height: 1.4;
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
    .form-group {
      margin-bottom: 1.2rem;
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
    .btn {
      width: 100%;
      padding: 0.75rem;
      border: none;
      border-radius: 6px;
      font-size: 1rem;
      font-weight: 600;
      cursor: pointer;
      margin-top: 0.5rem;
      background-color: #2563eb;
      color: #ffffff;
      transition: background-color 0.2s;
    }
    .btn:hover:not(:disabled) {
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
export class ForgotPasswordComponent {
  forgotForm: FormGroup;
  isLoading = signal(false);
  errorMessage = signal('');
  successMessage = signal('');

  constructor(
    private fb: FormBuilder,
    private authService: AuthService
  ) {
    this.forgotForm = this.fb.group({
      email: ['', [Validators.required, Validators.email]]
    });
  }

  get f() {
    return this.forgotForm.controls;
  }

  onSubmit(): void {
    if (this.forgotForm.invalid) return;

    this.isLoading.set(true);
    this.errorMessage.set('');
    this.successMessage.set('');

    this.authService.forgotPassword(this.forgotForm.value.email).subscribe({
      next: res => {
        this.isLoading.set(false);
        this.successMessage.set(res.message || 'Nếu email tồn tại trong hệ thống, hướng dẫn đặt lại mật khẩu đã được gửi đến hòm thư của bạn.');
      },
      error: err => {
        this.isLoading.set(false);
        this.errorMessage.set(err.error?.message || 'Đã có lỗi xảy ra khi xử lý yêu cầu.');
      }
    });
  }
}

