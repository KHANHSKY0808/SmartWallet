import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { NotificationService } from '../../../core/services/notification.service';
import { NotificationDto } from '../../../core/models/notification.models';

@Component({
  selector: 'app-notification-bell',
  standalone: true,
  imports: [CommonModule, RouterModule],
  template: `
    <div class="notification-container" style="position: relative; display: inline-block;">
      <!-- Bell Icon Button -->
      <button (click)="toggleDropdown()" 
              style="background: rgba(255,255,255,0.15); border: none; color: white; padding: 0.4rem 0.65rem; border-radius: 6px; cursor: pointer; display: flex; align-items: center; position: relative;">
        <span style="font-size: 1.15rem;">🔔</span>
        <span *ngIf="unreadCount() > 0" 
              class="unread-badge"
              style="position: absolute; top: -4px; right: -4px; background: #ef4444; color: white; border-radius: 9999px; font-size: 0.65rem; font-weight: 700; min-width: 16px; height: 16px; display: flex; align-items: center; justify-content: center; padding: 0 2px;">
          {{ unreadCount() > 99 ? '99+' : unreadCount() }}
        </span>
      </button>

      <!-- Dropdown Popover -->
      <div *ngIf="isOpen()" 
           class="notification-dropdown"
           style="position: absolute; right: 0; top: calc(100% + 8px); width: min(340px, calc(100vw - 1.5rem)); background: white; border: 1px solid #e2e8f0; border-radius: 10px; box-shadow: 0 10px 25px -5px rgba(0,0,0,0.15); z-index: 1000; overflow: hidden;">
        
        <div style="display: flex; justify-content: space-between; align-items: center; padding: 0.75rem 1rem; border-bottom: 1px solid #f1f5f9; background: #f8fafc;">
          <h4 style="margin: 0; font-size: 0.95rem; font-weight: 700; color: #0f172a;">🔔 Thông báo</h4>
          <button *ngIf="unreadCount() > 0" 
                  (click)="markAllAsRead()"
                  style="background: none; border: none; color: #2563eb; font-size: 0.75rem; font-weight: 600; cursor: pointer; padding: 0;">
            Đã đọc tất cả
          </button>
        </div>

        <div style="max-height: 350px; overflow-y: auto;">
          <!-- F-04: Empty state khi danh sách rỗng -->
          <div *ngIf="notifications().length === 0" 
               class="empty-notifications"
               style="padding: 2.5rem 1rem; text-align: center; color: #94a3b8; font-size: 0.85rem;">
            <span style="font-size: 2rem; display: block; margin-bottom: 0.5rem;">🔕</span>
            Không có thông báo nào
          </div>

          <!-- F-04: Danh sách có dữ liệu, mới nhất trước -->
          <div *ngFor="let n of notifications()" 
               class="notification-item"
               [style.background]="n.isRead ? 'white' : '#f0fdf4'"
               style="padding: 0.75rem 1rem; border-bottom: 1px solid #f1f5f9; cursor: pointer; transition: background 0.2s;"
               (click)="onNotificationClick(n)">
            <div style="display: flex; justify-content: space-between; align-items: flex-start; gap: 0.5rem;">
              <h5 style="margin: 0 0 0.25rem; font-size: 0.85rem; font-weight: 600; color: #0f172a;">
                {{ n.title }}
              </h5>
              <span *ngIf="!n.isRead" style="width: 8px; height: 8px; border-radius: 50%; background: #22c55e; flex-shrink: 0; margin-top: 4px;"></span>
            </div>
            <p style="margin: 0 0 0.25rem; font-size: 0.8rem; color: #475569; line-height: 1.35;">
              {{ n.message }}
            </p>
            <span style="font-size: 0.7rem; color: #94a3b8;">
              {{ n.createdAt | date:'HH:mm dd/MM/yyyy' }}
            </span>
          </div>
        </div>
      </div>
    </div>
  `
})
export class NotificationBellComponent implements OnInit {
  private notificationService = inject(NotificationService);

  isOpen = signal<boolean>(false);
  notifications = signal<NotificationDto[]>([]);
  unreadCount = signal<number>(0);

  ngOnInit() {
    this.loadNotifications();
  }

  loadNotifications() {
    this.notificationService.getUnreadCount().subscribe({
      next: (r) => {
        if (typeof r?.count === 'number') {
          this.unreadCount.set(r.count);
        }
      },
      error: () => {}
    });

    this.notificationService.getNotifications({ page: 1, pageSize: 20 }).subscribe({
      next: (res) => {
        // F-04 / F-01: Sắp xếp mới nhất trước
        const items = res?.items || [];
        this.notifications.set(items);
        if (items.length > 0 && this.unreadCount() === 0) {
          this.unreadCount.set(items.filter(n => !n.isRead).length);
        }
      },
      error: () => {}
    });
  }

  toggleDropdown() {
    this.isOpen.update(v => !v);
    if (this.isOpen()) {
      this.loadNotifications();
    }
  }

  onNotificationClick(n: NotificationDto) {
    if (!n.isRead) {
      this.notificationService.markAsRead(n.id).subscribe({
        next: () => {
          n.isRead = true;
          this.unreadCount.update(c => Math.max(c - 1, 0));
        }
      });
    }
  }

  markAllAsRead() {
    this.notificationService.markAllAsRead().subscribe({
      next: () => {
        this.notifications.update(list => list.map(n => ({ ...n, isRead: true })));
        this.unreadCount.set(0);
      }
    });
  }
}

