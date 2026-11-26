import { Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators, AbstractControl } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';

function passwordMatchValidator(control: AbstractControl): { [key: string]: boolean } | null {
  const password = control.get('password');
  const confirmPassword = control.get('confirmPassword');
  if (password && confirmPassword && password.value !== confirmPassword.value) {
    return { passwordMismatch: true };
  }
  return null;
}

@Component({
  selector: 'app-register',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterModule],
  template: `
    <div class="auth-container">
      <div class="auth-card">
        <h2>Đăng ký tài khoản</h2>
        <p class="subtitle">Bắt đầu hành trình làm chủ tài chính cá nhân</p>

        <div *ngIf="successMessage()" class="alert alert-success">
          <strong>🎉 Thành công!</strong> {{ successMessage() }}
          <div style="margin-top: 0.75rem;">
            <button type="button"
                    (click)="onResendConfirmation()"
                    [disabled]="isResending()"
                    class="btn-resend"
                    style="padding: 0.45rem 0.9rem; background: #1d4ed8; color: white; border: none; border-radius: 6px; font-size: 0.85rem; font-weight: 600; cursor: pointer;">
              {{ isResending() ? 'Đang gửi...' : 'Gửi lại email xác nhận' }}
            </button>
          </div>
          <p *ngIf="resendMessage()" class="resend-feedback" style="margin: 0.5rem 0 0; font-size: 0.82rem; color: #1e40af;">
            {{ resendMessage() }}
          </p>
        </div>

        <div *ngIf="errorMessage()" class="alert alert-danger">
          {{ errorMessage() }}
        </div>

        <form *ngIf="!successMessage()" [formGroup]="registerForm" (ngSubmit)="onSubmit()">
          <div class="form-group">
            <label for="fullName">Họ và tên</label>
            <input
              id="fullName"
              type="text"
              formControlName="fullName"
              placeholder="Nguyễn Văn A"
            />
          </div>

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
              <span *ngIf="f['email'].errors['email']">Email không đúng định dạng</span>
            </div>
          </div>

          <div class="form-group">
            <label for="password">Mật khẩu</label>
            <input
              id="password"
              type="password"
              formControlName="password"
              placeholder="Ít nhất 8 ký tự"
              [class.is-invalid]="f['password'].touched && f['password'].invalid"
            />
            <div *ngIf="f['password'].touched && f['password'].errors" class="invalid-feedback">
              <span *ngIf="f['password'].errors['required']">Mật khẩu không được để trống</span>
              <span *ngIf="f['password'].errors['minlength']">Mật khẩu phải có ít nhất 8 ký tự</span>
            </div>
          </div>

          <div class="form-group">
            <label for="confirmPassword">Xác nhận mật khẩu</label>
            <input
              id="confirmPassword"
              type="password"
              formControlName="confirmPassword"
              placeholder="Nhập lại mật khẩu"
              [class.is-invalid]="f['confirmPassword'].touched && (f['confirmPassword'].invalid || registerForm.hasError('passwordMismatch'))"
            />
            <div *ngIf="f['confirmPassword'].touched && registerForm.hasError('passwordMismatch')" class="invalid-feedback">
              Mật khẩu xác nhận không khớp
            </div>
          </div>

          <button type="submit" [disabled]="registerForm.invalid || isLoading()" class="btn btn-primary">
            <span *ngIf="!isLoading()">Tạo tài khoản</span>
            <span *ngIf="isLoading()">Đang tạo tài khoản...</span>
          </button>
        </form>

        <div class="auth-footer">
          Đã có tài khoản? <a routerLink="/login">Đăng nhập</a>
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
    .btn {
      width: 100%;
      padding: 0.75rem;
      border: none;
      border-radius: 6px;
      font-size: 1rem;
      font-weight: 600;
      cursor: pointer;
      margin-top: 0.5rem;
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
export class RegisterComponent {
  registerForm: FormGroup;
  isLoading = signal(false);
  isResending = signal(false);
  errorMessage = signal('');
  successMessage = signal('');
  resendMessage = signal('');
  registeredEmail = signal('');

  constructor(
    private fb: FormBuilder,
    private authService: AuthService
  ) {
    this.registerForm = this.fb.group(
      {
        fullName: [''],
        email: ['', [Validators.required, Validators.email]],
        password: ['', [Validators.required, Validators.minLength(8)]],
        confirmPassword: ['', [Validators.required]]
      },
      { validators: passwordMatchValidator }
    );
  }

  get f() {
    return this.registerForm.controls;
  }

  onSubmit(): void {
    if (this.registerForm.invalid) return;

    this.isLoading.set(true);
    this.errorMessage.set('');
    this.successMessage.set('');
    this.resendMessage.set('');
    const submittedEmail = String(this.registerForm.value.email || '').trim();

    this.authService.register(this.registerForm.value).subscribe({
      next: res => {
        this.isLoading.set(false);
        this.registeredEmail.set(submittedEmail);
        this.successMessage.set(res.message || 'Đăng ký thành công! Vui lòng kiểm tra hòm thư email để kích hoạt tài khoản.');
      },
      error: err => {
        this.isLoading.set(false);
        if (err.status === 409) {
          this.errorMessage.set('Email đã được sử dụng. Vui lòng chọn email khác hoặc đăng nhập.');
        } else {
          this.errorMessage.set(err.error?.message || 'Đã có lỗi xảy ra khi đăng ký tài khoản.');
        }
      }
    });
  }

  onResendConfirmation(): void {
    const email = this.registeredEmail() || String(this.registerForm.value.email || '').trim();
    if (!email) return;

    this.isResending.set(true);
    this.resendMessage.set('');
    this.authService.resendConfirmation(email).subscribe({
      next: res => {
        this.isResending.set(false);
        this.resendMessage.set(res.message || 'Đã gửi lại yêu cầu email xác nhận.');
      },
      error: err => {
        this.isResending.set(false);
        this.resendMessage.set(err.error?.message || 'Không thể gửi lại email lúc này. Vui lòng thử lại sau.');
      }
    });
  }
}

