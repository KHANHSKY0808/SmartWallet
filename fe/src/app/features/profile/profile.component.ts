import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators, AbstractControl } from '@angular/forms';
import { AuthService } from '../../core/services/auth.service';
import { ProfileService } from '../../core/services/profile.service';
import { Currency, UserProfile } from '../../core/models/user-profile.model';

function passwordMatchValidator(control: AbstractControl): { [key: string]: boolean } | null {
  const newPassword = control.get('newPassword');
  const confirmPassword = control.get('confirmPassword');
  if (newPassword && confirmPassword && newPassword.value !== confirmPassword.value) {
    return { passwordMismatch: true };
  }
  return null;
}

@Component({
  selector: 'app-profile',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  template: `
    <div class="profile-container">
      <div class="profile-layout">
        <!-- Sidebar hồ sơ tóm tắt -->
        <div class="card profile-summary">
          <div class="avatar-wrapper">
            <img
              [src]="avatarPreview || profile?.avatarUrl || defaultAvatar"
              alt="Avatar"
              class="avatar-img"
            />
            <label class="avatar-upload-btn" for="avatar-input">
              📷 Đổi ảnh
              <input
                id="avatar-input"
                type="file"
                (change)="onAvatarSelected($event)"
                accept="image/png, image/jpeg, image/jpg"
                hidden
              />
            </label>
          </div>
          <h3>{{ profile?.fullName || 'Người dùng SmartWallet' }}</h3>
          <p class="text-muted">{{ authService.currentUser()?.email }}</p>
          <span class="badge">{{ authService.currentUserRole() || 'User' }}</span>
        </div>

        <!-- Nội dung chính: Cập nhật thông tin & Đổi mật khẩu -->
        <div class="main-content">
          <!-- Thông báo -->
          <div *ngIf="successMessage" class="alert alert-success">
            ✅ {{ successMessage }}
          </div>
          <div *ngIf="errorMessage" class="alert alert-danger">
            ❌ {{ errorMessage }}
          </div>

          <!-- Form thông tin cá nhân -->
          <div class="card section-card">
            <h4>Thông tin cá nhân & Tùy chọn</h4>
            <form [formGroup]="profileForm" (ngSubmit)="onUpdateProfile()">
              <div class="form-group">
                <label for="fullName">Họ và tên</label>
                <input
                  id="fullName"
                  type="text"
                  formControlName="fullName"
                  class="form-control"
                />
              </div>

              <div class="form-row">
                <div class="form-group col">
                  <label for="currency">Đơn vị tiền tệ chính</label>
                  <select id="currency" formControlName="defaultCurrency" class="form-control">
                    <option *ngFor="let c of currencies" [value]="c.code">
                      {{ c.code }} ({{ c.symbol }}) - {{ c.name }}
                    </option>
                  </select>
                </div>

                <div class="form-group col">
                  <label for="language">Ngôn ngữ</label>
                  <select id="language" formControlName="language" class="form-control">
                    <option value="vi">Tiếng Việt</option>
                    <option value="en">English</option>
                  </select>
                </div>
              </div>

              <button type="submit" [disabled]="profileForm.invalid || isUpdating" class="btn btn-primary">
                <span *ngIf="!isUpdating">Lưu thông tin</span>
                <span *ngIf="isUpdating">Đang lưu...</span>
              </button>
            </form>
          </div>

          <!-- Form đổi mật khẩu -->
          <div class="card section-card">
            <h4>Đổi mật khẩu</h4>
            <form [formGroup]="passwordForm" (ngSubmit)="onChangePassword()">
              <div class="form-group">
                <label for="currentPassword">Mật khẩu hiện tại</label>
                <input
                  id="currentPassword"
                  type="password"
                  formControlName="currentPassword"
                  class="form-control"
                />
              </div>

              <div class="form-row">
                <div class="form-group col">
                  <label for="newPassword">Mật khẩu mới (ít nhất 8 ký tự)</label>
                  <input
                    id="newPassword"
                    type="password"
                    formControlName="newPassword"
                    class="form-control"
                  />
                </div>

                <div class="form-group col">
                  <label for="confirmPassword">Nhập lại mật khẩu mới</label>
                  <input
                    id="confirmPassword"
                    type="password"
                    formControlName="confirmPassword"
                    class="form-control"
                  />
                </div>
              </div>

              <button type="submit" [disabled]="passwordForm.invalid || isChangingPassword" class="btn btn-secondary">
                <span *ngIf="!isChangingPassword">Đổi mật khẩu</span>
                <span *ngIf="isChangingPassword">Đang cập nhật...</span>
              </button>
            </form>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .profile-container {
      max-width: 960px;
      margin: 2rem auto;
      padding: 0 1rem;
    }
    .profile-layout {
      display: grid;
      grid-template-columns: 280px 1fr;
      gap: 1.5rem;
    }
    @media (max-width: 768px) {
      .profile-layout {
        grid-template-columns: 1fr;
      }
    }
    .card {
      background: #ffffff;
      border-radius: 8px;
      box-shadow: 0 2px 8px rgba(0,0,0,0.06);
      padding: 1.5rem;
    }
    .profile-summary {
      text-align: center;
      height: fit-content;
    }
    .avatar-wrapper {
      position: relative;
      width: 120px;
      height: 120px;
      margin: 0 auto 1rem;
    }
    .avatar-img {
      width: 100%;
      height: 100%;
      border-radius: 50%;
      object-fit: cover;
      border: 3px solid #e2e8f0;
    }
    .avatar-upload-btn {
      position: absolute;
      bottom: 0;
      right: 0;
      background: #2563eb;
      color: white;
      font-size: 0.75rem;
      padding: 0.35rem 0.65rem;
      border-radius: 20px;
      cursor: pointer;
      box-shadow: 0 2px 4px rgba(0,0,0,0.2);
    }
    .text-muted {
      color: #64748b;
      font-size: 0.85rem;
      margin: 0.25rem 0 0.75rem 0;
    }
    .badge {
      display: inline-block;
      padding: 0.25rem 0.75rem;
      background: #eff6ff;
      color: #2563eb;
      font-weight: 600;
      font-size: 0.8rem;
      border-radius: 12px;
    }
    .main-content {
      display: flex;
      flex-direction: column;
      gap: 1.5rem;
    }
    .section-card h4 {
      margin: 0 0 1.25rem 0;
      color: #1e293b;
      font-size: 1.15rem;
      border-bottom: 1px solid #f1f5f9;
      padding-bottom: 0.75rem;
    }
    .form-group {
      margin-bottom: 1rem;
    }
    .form-row {
      display: flex;
      gap: 1rem;
    }
    .form-row .col {
      flex: 1;
    }
    label {
      display: block;
      margin-bottom: 0.4rem;
      font-size: 0.875rem;
      font-weight: 600;
      color: #334155;
    }
    .form-control {
      width: 100%;
      box-sizing: border-box;
      padding: 0.65rem 0.85rem;
      border: 1px solid #cbd5e1;
      border-radius: 6px;
      font-size: 0.95rem;
    }
    .form-control:focus {
      outline: none;
      border-color: #2563eb;
    }
    .btn {
      padding: 0.65rem 1.25rem;
      border: none;
      border-radius: 6px;
      font-weight: 600;
      cursor: pointer;
      transition: background-color 0.2s;
    }
    .btn-primary {
      background: #2563eb;
      color: #ffffff;
    }
    .btn-primary:hover:not(:disabled) {
      background: #1d4ed8;
    }
    .btn-secondary {
      background: #475569;
      color: #ffffff;
    }
    .btn-secondary:hover:not(:disabled) {
      background: #334155;
    }
    .btn:disabled {
      opacity: 0.6;
      cursor: not-allowed;
    }
    .alert {
      padding: 0.85rem 1rem;
      border-radius: 6px;
      font-size: 0.875rem;
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
  `]
})
export class ProfileComponent implements OnInit {
  profile: UserProfile | null = null;
  currencies: Currency[] = [
    { code: 'VND', name: 'Việt Nam Đồng', symbol: '₫', isActive: true },
    { code: 'USD', name: 'Đô la Mỹ', symbol: '$', isActive: true },
    { code: 'EUR', name: 'Euro', symbol: '€', isActive: true },
    { code: 'JPY', name: 'Yên Nhật', symbol: '¥', isActive: true }
  ];

  profileForm: FormGroup;
  passwordForm: FormGroup;
  avatarPreview: string | null = null;

  isUpdating = false;
  isChangingPassword = false;
  successMessage = '';
  errorMessage = '';

  defaultAvatar = 'https://ui-avatars.com/api/?name=User&background=2563eb&color=fff';

  constructor(
    private fb: FormBuilder,
    public authService: AuthService,
    private profileService: ProfileService
  ) {
    this.profileForm = this.fb.group({
      fullName: ['', [Validators.required, Validators.maxLength(100)]],
      defaultCurrency: ['VND'],
      language: ['vi']
    });

    this.passwordForm = this.fb.group(
      {
        currentPassword: ['', [Validators.required]],
        newPassword: ['', [Validators.required, Validators.minLength(8)]],
        confirmPassword: ['', [Validators.required]]
      },
      { validators: passwordMatchValidator }
    );
  }

  ngOnInit(): void {
    this.loadProfile();
    this.loadCurrencies();
  }

  loadProfile(): void {
    this.profileService.getProfile().subscribe({
      next: p => {
        this.profile = p;
        this.profileForm.patchValue({
          fullName: p.fullName,
          defaultCurrency: p.defaultCurrency,
          language: p.language
        });
        if (p.fullName) {
          this.defaultAvatar = `https://ui-avatars.com/api/?name=${encodeURIComponent(p.fullName)}&background=2563eb&color=fff`;
        }
      },
      error: err => {
        this.errorMessage = 'Không thể tải thông tin hồ sơ người dùng';
      }
    });
  }

  loadCurrencies(): void {
    this.profileService.getCurrencies().subscribe({
      next: list => {
        if (list && list.length > 0) {
          this.currencies = list;
        }
      },
      error: () => {}
    });
  }

  onUpdateProfile(): void {
    if (this.profileForm.invalid) return;

    this.isUpdating = true;
    this.successMessage = '';
    this.errorMessage = '';

    this.profileService.updateProfile(this.profileForm.value).subscribe({
      next: updated => {
        this.isUpdating = false;
        this.profile = updated;
        this.successMessage = 'Cập nhật thông tin hồ sơ thành công!';
      },
      error: err => {
        this.isUpdating = false;
        this.errorMessage = err.error?.message || 'Lỗi khi cập nhật hồ sơ';
      }
    });
  }

  onAvatarSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      const file = input.files[0];

      if (file.size > 5 * 1024 * 1024) {
        this.errorMessage = 'Kích thước ảnh đại diện không được vượt quá 5MB';
        return;
      }

      this.profileService.uploadAvatar(file).subscribe({
        next: res => {
          this.avatarPreview = res.avatarUrl;
          this.successMessage = 'Tải ảnh đại diện thành công!';
        },
        error: err => {
          this.errorMessage = err.error?.message || 'Lỗi khi tải ảnh đại diện lên máy chủ';
        }
      });
    }
  }

  onChangePassword(): void {
    if (this.passwordForm.invalid) return;

    this.isChangingPassword = true;
    this.successMessage = '';
    this.errorMessage = '';

    this.authService.changePassword(this.passwordForm.value).subscribe({
      next: res => {
        this.isChangingPassword = false;
        this.successMessage = res.message || 'Đổi mật khẩu thành công!';
        this.passwordForm.reset();
      },
      error: err => {
        this.isChangingPassword = false;
        this.errorMessage = err.error?.message || 'Mật khẩu hiện tại không chính xác';
      }
    });
  }
}

