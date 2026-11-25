import { describe, it, expect, beforeEach, vi } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { NotificationBellComponent } from './notification-bell.component';
import { NotificationService } from '../../../core/services/notification.service';
import { NotificationDto } from '../../../core/models/notification.models';
import { PagedResult } from '../../../core/models/transaction.models';

describe('NotificationBellComponent (F-04)', () => {
  let component: NotificationBellComponent;
  let fixture: ComponentFixture<NotificationBellComponent>;
  let notificationServiceMock: {
    getNotifications: ReturnType<typeof vi.fn>;
    getUnreadCount: ReturnType<typeof vi.fn>;
    markAsRead: ReturnType<typeof vi.fn>;
    markAllAsRead: ReturnType<typeof vi.fn>;
  };

  const mockNotifications: NotificationDto[] = [
    {
      id: 'n1',
      userId: 'u1',
      type: 'BudgetAlert',
      title: '⚠️ Ngân sách Ăn uống đã sử dụng 100%',
      message: 'Bạn đã chi 2.000.000 đ / 2.000.000 đ',
      isRead: false,
      createdAt: '2026-11-24T09:00:00Z'
    },
    {
      id: 'n2',
      userId: 'u1',
      type: 'BudgetAlert',
      title: '⚠️ Ngân sách Ăn uống đã sử dụng 80%',
      message: 'Bạn đã chi 1.600.000 đ / 2.000.000 đ',
      isRead: true,
      createdAt: '2026-11-24T08:00:00Z'
    }
  ];

  beforeEach(async () => {
    notificationServiceMock = {
      getNotifications: vi.fn().mockReturnValue(
        of({ items: mockNotifications, totalItems: 2, page: 1, pageSize: 20, totalPages: 1 })
      ),
      getUnreadCount: vi.fn().mockReturnValue(of({ count: 1 })),
      markAsRead: vi.fn().mockReturnValue(of({ message: 'OK' })),
      markAllAsRead: vi.fn().mockReturnValue(of({ message: 'OK', count: 1 }))
    };

    await TestBed.configureTestingModule({
      imports: [NotificationBellComponent],
      providers: [
        { provide: NotificationService, useValue: notificationServiceMock }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(NotificationBellComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('F-04: Should show unread badge count correctly', () => {
    expect(component.unreadCount()).toBe(1);
  });

  it('F-04: Should display notifications sorted newest first when opened', () => {
    component.toggleDropdown();
    fixture.detectChanges();

    expect(component.isOpen()).toBe(true);
    const notifs = component.notifications();
    expect(notifs.length).toBe(2);
    expect(notifs[0].title).toContain('100%');
    expect(notifs[1].title).toContain('80%');
  });

  it('F-04: Should display empty state message when notification list is empty', () => {
    notificationServiceMock.getNotifications.mockReturnValue(
      of({ items: [], totalItems: 0, page: 1, pageSize: 20, totalPages: 0 })
    );

    component.loadNotifications();
    component.isOpen.set(true);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const emptyEl = el.querySelector('.empty-notifications');
    expect(emptyEl).toBeTruthy();
    expect(emptyEl?.textContent).toContain('Không có thông báo nào');
  });
});
