import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AdminService, AdminUserItem, RoleMatrixItem } from '../../../core/services/admin.service';
import { AuthService } from '../../../core/services/auth.service';

@Component({
  selector: 'app-admin-users',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div style="display: flex; flex-direction: column; gap: 1.25rem; max-width: 100%;">
      <div style="display: flex; flex-wrap: wrap; justify-content: space-between; align-items: center; gap: 0.75rem;">
        <div>
          <h2 style="margin: 0; font-size: 1.4rem; color: #0f172a;">Quản trị người dùng & Phân quyền RBAC</h2>
          <p style="margin: 0.25rem 0 0; color: #64748b; font-size: 0.9rem;">Tìm kiếm, khóa/mở khóa tài khoản tức thì và gán vai trò.</p>
        </div>
        <button (click)="loadUsers()" style="padding: 0.5rem 1rem; background: #2563eb; color: white; border: none; border-radius: 6px; font-weight: 600; cursor: pointer;">
          Làm mới
        </button>
      </div>

      <div *ngIf="errorMsg()" class="alert-error" style="padding: 0.75rem 1rem; background: #fee2e2; color: #991b1b; border: 1px solid #fecaca; border-radius: 8px;">
        ⚠️ {{ errorMsg() }}
      </div>
      <div *ngIf="successMsg()" class="alert-success" style="padding: 0.75rem 1rem; background: #dcfce7; color: #166534; border: 1px solid #bbf7d0; border-radius: 8px;">
        ✅ {{ successMsg() }}
      </div>

      <!-- Bộ lọc -->
      <div style="display: flex; flex-wrap: wrap; gap: 0.75rem; background: white; padding: 1rem; border-radius: 10px; box-shadow: 0 1px 3px rgba(0,0,0,0.08);">
        <input type="text" [(ngModel)]="searchQuery" (keyup.enter)="applyFilter()" placeholder="Tìm email hoặc họ tên..."
               style="flex: 1 1 200px; min-width: 0; padding: 0.5rem 0.75rem; border: 1px solid #cbd5e1; border-radius: 6px;" />
        <select [(ngModel)]="statusFilter" (change)="applyFilter()"
                style="flex: 0 1 180px; padding: 0.5rem 0.75rem; border: 1px solid #cbd5e1; border-radius: 6px;">
          <option value="">Tất cả trạng thái</option>
          <option value="Active">Active</option>
          <option value="Locked">Locked</option>
          <option value="PendingConfirm">PendingConfirm</option>
        </select>
        <button (click)="applyFilter()" style="padding: 0.5rem 1rem; background: #0f172a; color: white; border: none; border-radius: 6px; font-weight: 600; cursor: pointer;">
          Tìm kiếm
        </button>
      </div>

      <!-- Bảng người dùng -->
      <div style="background: white; border-radius: 10px; box-shadow: 0 1px 3px rgba(0,0,0,0.08); overflow-x: auto;">
        <table style="width: 100%; border-collapse: collapse; min-width: 640px;">
          <thead>
            <tr style="background: #f8fafc; border-bottom: 1px solid #e2e8f0; text-align: left;">
              <th style="padding: 0.75rem 1rem; font-size: 0.85rem; color: #475569;">Email</th>
              <th style="padding: 0.75rem 1rem; font-size: 0.85rem; color: #475569;">Họ tên</th>
              <th style="padding: 0.75rem 1rem; font-size: 0.85rem; color: #475569;">Trạng thái</th>
              <th style="padding: 0.75rem 1rem; font-size: 0.85rem; color: #475569;">Vai trò</th>
              <th *ngIf="canLock() || canWriteRoles()" style="padding: 0.75rem 1rem; font-size: 0.85rem; color: #475569;">Thao tác</th>
            </tr>
          </thead>
          <tbody>
            <tr *ngFor="let u of users()" style="border-bottom: 1px solid #f1f5f9;">
              <td style="padding: 0.75rem 1rem; font-size: 0.9rem; font-weight: 500;">{{ u.email }}</td>
              <td style="padding: 0.75rem 1rem; font-size: 0.9rem;">{{ u.fullName }}</td>
              <td style="padding: 0.75rem 1rem;">
                <span [style.background]="u.status === 'Locked' ? '#fee2e2' : (u.status === 'Active' ? '#dcfce7' : '#fef9c3')"
                      [style.color]="u.status === 'Locked' ? '#991b1b' : (u.status === 'Active' ? '#166534' : '#854d0e')"
                      style="padding: 0.2rem 0.6rem; border-radius: 9999px; font-size: 0.75rem; font-weight: 700;">
                  {{ u.status }}
                </span>
              </td>
              <td style="padding: 0.75rem 1rem;">
                <ng-container *ngIf="canWriteRoles(); else readOnlyRoles">
                  <select [ngModel]="u.roles[0] || 'User'" (ngModelChange)="onRoleSelect(u, $event)"
                          style="padding: 0.35rem 0.5rem; border: 1px solid #cbd5e1; border-radius: 6px; font-size: 0.85rem;">
                    <option *ngFor="let r of roles()" [value]="r.name">{{ r.name }}</option>
                  </select>
                </ng-container>
                <ng-template #readOnlyRoles>
                  <span style="font-size: 0.85rem; font-weight: 600;">{{ u.roles.join(', ') }}</span>
                </ng-template>
              </td>
              <td *ngIf="canLock() || canWriteRoles()" style="padding: 0.75rem 1rem;">
                <button *ngIf="canLock() && u.status !== 'Locked'" (click)="openLockConfirm(u)"
                        class="btn-lock"
                        style="padding: 0.35rem 0.75rem; background: #dc2626; color: white; border: none; border-radius: 6px; font-size: 0.8rem; cursor: pointer;">
                  Khóa
                </button>
                <button *ngIf="canLock() && u.status === 'Locked'" (click)="unlock(u)"
                        class="btn-unlock"
                        style="padding: 0.35rem 0.75rem; background: #16a34a; color: white; border: none; border-radius: 6px; font-size: 0.8rem; cursor: pointer;">
                  Mở khóa
                </button>
              </td>
            </tr>
            <tr *ngIf="users().length === 0">
              <td colspan="5" style="padding: 2rem; text-align: center; color: #94a3b8;">Không tìm thấy người dùng nào.</td>
            </tr>
          </tbody>
        </table>
      </div>

      <!-- Hộp xác nhận khóa (F-02) -->
      <div *ngIf="lockTarget()" class="confirm-modal" style="position: fixed; inset: 0; background: rgba(15,23,42,0.5); display: flex; align-items: center; justify-content: center; padding: 1rem; z-index: 1200;">
        <div style="background: white; border-radius: 10px; padding: 1.25rem; width: 100%; max-width: 400px; box-sizing: border-box;">
          <h3 style="margin: 0 0 0.5rem;">Xác nhận khóa tài khoản</h3>
          <p style="margin: 0 0 0.75rem; font-size: 0.9rem; color: #475569;">Tài khoản: <strong>{{ lockTarget()?.email }}</strong></p>
          <input type="text" [(ngModel)]="lockReason" placeholder="Lý do khóa tài khoản..."
                 style="width: 100%; box-sizing: border-box; padding: 0.5rem; border: 1px solid #cbd5e1; border-radius: 6px; margin-bottom: 1rem;" />
          <div style="display: flex; justify-content: flex-end; gap: 0.5rem;">
            <button (click)="lockTarget.set(null)" style="padding: 0.45rem 0.9rem; background: #e2e8f0; border: none; border-radius: 6px; cursor: pointer;">Hủy</button>
            <button (click)="confirmLock()" class="btn-confirm-lock" style="padding: 0.45rem 0.9rem; background: #dc2626; color: white; border: none; border-radius: 6px; cursor: pointer;">Xác nhận khóa</button>
          </div>
        </div>
      </div>

      <!-- Ma trận vai trò - quyền (F-03) -->
      <div style="background: white; border-radius: 10px; padding: 1rem; box-shadow: 0 1px 3px rgba(0,0,0,0.08); overflow-x: auto;">
        <h3 style="margin: 0 0 0.75rem; font-size: 1.05rem;">Ma trận Vai trò – Quyền (RBAC)</h3>
        <table style="width: 100%; border-collapse: collapse; min-width: 520px;">
          <thead>
            <tr style="background: #f8fafc; border-bottom: 1px solid #e2e8f0; text-align: left;">
              <th style="padding: 0.6rem 0.75rem; font-size: 0.85rem;">Vai trò</th>
              <th style="padding: 0.6rem 0.75rem; font-size: 0.85rem;">Quyền hạn</th>
            </tr>
          </thead>
          <tbody>
            <tr *ngFor="let r of roles()" style="border-bottom: 1px solid #f1f5f9;">
              <td style="padding: 0.6rem 0.75rem; font-weight: 700;">{{ r.name }}</td>
              <td style="padding: 0.6rem 0.75rem; font-size: 0.85rem; color: #334155;">
                {{ r.permissions.length > 0 ? r.permissions.join(', ') : '(Không có quyền quản trị)' }}
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  `
})
export class AdminUsersComponent implements OnInit {
  private adminService = inject(AdminService);
  private authService = inject(AuthService);

  users = signal<AdminUserItem[]>([]);
  roles = signal<RoleMatrixItem[]>([]);
  errorMsg = signal('');
  successMsg = signal('');
  lockTarget = signal<AdminUserItem | null>(null);
  lockReason = '';
  searchQuery = '';
  statusFilter = '';
  page = 1;

  canLock = () => this.authService.hasPermission('users.lock');
  canWriteRoles = () => this.authService.hasPermission('users.roles.write');

  ngOnInit(): void {
    this.loadRoles();
    this.loadUsers();
  }

  loadRoles(): void {
    this.adminService.getRoles().subscribe({
      next: res => this.roles.set(res || []),
      error: () => {}
    });
  }

  applyFilter(): void {
    this.page = 1;
    this.loadUsers();
  }

  loadUsers(): void {
    this.errorMsg.set('');
    this.adminService.getUsers({ q: this.searchQuery, status: this.statusFilter, page: this.page, pageSize: 20 }).subscribe({
      next: res => this.users.set(res?.items || []),
      error: err => this.errorMsg.set(err.error?.message || 'Không thể tải danh sách người dùng.')
    });
  }

  openLockConfirm(u: AdminUserItem): void {
    this.lockTarget.set(u);
    this.lockReason = '';
    this.errorMsg.set('');
    this.successMsg.set('');
  }

  confirmLock(): void {
    const target = this.lockTarget();
    if (!target) return;
    this.adminService.lockUser(target.id, this.lockReason).subscribe({
      next: () => {
        this.lockTarget.set(null);
        this.successMsg.set(`Đã khóa tài khoản ${target.email}.`);
        this.loadUsers();
      },
      error: err => {
        this.lockTarget.set(null);
        this.errorMsg.set(err.error?.message || 'Không thể khóa tài khoản.');
      }
    });
  }

  unlock(u: AdminUserItem): void {
    this.errorMsg.set('');
    this.successMsg.set('');
    this.adminService.unlockUser(u.id).subscribe({
      next: () => {
        this.successMsg.set(`Đã mở khóa tài khoản ${u.email}.`);
        this.loadUsers();
      },
      error: err => this.errorMsg.set(err.error?.message || 'Không thể mở khóa tài khoản.')
    });
  }

  onRoleSelect(u: AdminUserItem, roleName: string): void {
    this.errorMsg.set('');
    this.successMsg.set('');
    this.adminService.updateUserRoles(u.id, [roleName]).subscribe({
      next: () => {
        this.successMsg.set(`Đã cập nhật vai trò ${roleName} cho ${u.email}.`);
        this.loadUsers();
      },
      error: err => this.errorMsg.set(err.error?.message || 'Không thể đổi vai trò.')
    });
  }
}

