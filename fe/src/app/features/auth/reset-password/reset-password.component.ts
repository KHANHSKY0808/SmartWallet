import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators, AbstractControl } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';

function passwordMatchValidator(control: AbstractControl): { [key: string]: boolean } | null {
  const newPassword = control.get('newPassword');
  const confirmPassword = control.get('confirmPassword');
  if (newPassword && confirmPassword && newPassword.value !== confirmPassword.value) {
    return { passwordMismatch: true };
  }
  return null;
}

@Component({
  selector: 'app-reset-password',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterModule],
  template: `
    <div class="auth-container">
      <div class="auth-card">
        <h2>Đặt lại mật khẩu mới</h2>
        <p class="subtitle">Tạo mật khẩu an toàn mới cho tài khoản của bạn</p>

        <div *ngIf="successMessage()" class="alert alert-success">
          <strong>🎉 Thành công!</strong> {{ successMessage() }}
          <div style="margin-top: 1rem;">
            <a routerLink="/login" class="btn btn-primary">Đăng nhập với mật khẩu mới</a>
          </div>
        </div>

        <div *ngIf="errorMessage()" class="alert alert-danger">
          {{ errorMessage() }}
        </div>

        <form *ngIf="!successMessage()" [formGroup]="resetForm" (ngSubmit)="onSubmit()">
          <div class="form-group">
            <label for="newPassword">Mật khẩu mới</label>
            <input
              id="newPassword"
              type="password"
              formControlName="newPassword"
              placeholder="Ít nhất 8 ký tự"
              [class.is-invalid]="f['newPassword'].touched && f['newPassword'].invalid"
            />
            <div *ngIf="f['newPassword'].touched && f['newPassword'].errors" class="invalid-feedback">
              <span *ngIf="f['newPassword'].errors['required']">Mật khẩu mới không được để trống</span>
              <span *ngIf="f['newPassword'].errors['minlength']">Mật khẩu phải có ít nhất 8 ký tự</span>
            </div>
          </div>

          <div class="form-group">
            <label for="confirmPassword">Xác nhận mật khẩu mới</label>
            <input
              id="confirmPassword"
              type="password"
              formControlName="confirmPassword"
              placeholder="Nhập lại mật khẩu mới"
              [class.is-invalid]="f['confirmPassword'].touched && (f['confirmPassword'].invalid || resetForm.hasError('passwordMismatch'))"
            />
            <div *ngIf="f['confirmPassword'].touched && resetForm.hasError('passwordMismatch')" class="invalid-feedback">
              Mật khẩu xác nhận không khớp
            </div>
          </div>

          <button type="submit" [disabled]="resetForm.invalid || isLoading()" class="btn btn-primary">
            <span *ngIf="!isLoading()">Xác nhận đổi mật khẩu</span>
            <span *ngIf="isLoading()">Đang lưu...</span>
          </button>
        </form>

        <div class="auth-footer">
          <a routerLink="/login">Quay lại trang Đăng nhập</a>
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
      text-decoration: none;
      display: inline-block;
      text-align: center;
      box-sizing: border-box;
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
export class ResetPasswordComponent implements OnInit {
  resetForm: FormGroup;
  token = '';
  isLoading = signal(false);
  errorMessage = signal('');
  successMessage = signal('');

  constructor(
    private fb: FormBuilder,
    private route: ActivatedRoute,
    private authService: AuthService
  ) {
    this.resetForm = this.fb.group(
      {
        newPassword: ['', [Validators.required, Validators.minLength(8)]],
        confirmPassword: ['', [Validators.required]]
      },
      { validators: passwordMatchValidator }
    );
  }

  ngOnInit(): void {
    this.token = this.route.snapshot.queryParamMap.get('token') || '';
    if (!this.token) {
      this.errorMessage.set('Không tìm thấy mã đặt lại mật khẩu trong liên kết.');
    }
  }

  get f() {
    return this.resetForm.controls;
  }

  onSubmit(): void {
    if (this.resetForm.invalid || !this.token) return;

    this.isLoading.set(true);
    this.errorMessage.set('');
    this.successMessage.set('');

    const payload = {
      token: this.token,
      newPassword: this.resetForm.value.newPassword,
      confirmPassword: this.resetForm.value.confirmPassword
    };

    this.authService.resetPassword(payload).subscribe({
      next: res => {
        this.isLoading.set(false);
        this.successMessage.set(res.message || 'Mật khẩu của bạn đã được đặt lại thành công!');
      },
      error: err => {
        this.isLoading.set(false);
        this.errorMessage.set(err.error?.message || 'Mã đặt lại mật khẩu không hợp lệ hoặc đã hết hạn.');
      }
    });
  }
}

