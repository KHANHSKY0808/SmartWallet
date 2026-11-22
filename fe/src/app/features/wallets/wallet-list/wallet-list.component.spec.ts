import { describe, it, expect, beforeEach, vi } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { WalletListComponent } from './wallet-list.component';
import { WalletService } from '../../../core/services/wallet.service';
import { WalletDto, TotalAssetsResponse } from '../../../core/models/wallet.models';

describe('WalletListComponent', () => {
  let component: WalletListComponent;
  let fixture: ComponentFixture<WalletListComponent>;
  let walletServiceMock: {
    getWallets: ReturnType<typeof vi.fn>;
    getTotalAssets: ReturnType<typeof vi.fn>;
    deleteWallet: ReturnType<typeof vi.fn>;
  };

  beforeEach(async () => {
    walletServiceMock = {
      getWallets: vi.fn().mockReturnValue(of([])),
      getTotalAssets: vi.fn().mockReturnValue(of({
        totalAssetsVnd: 0,
        ratesUnavailable: false,
        excludedWallets: []
      } as TotalAssetsResponse)),
      deleteWallet: vi.fn().mockReturnValue(of({ message: 'Deleted' }))
    };

    await TestBed.configureTestingModule({
      imports: [WalletListComponent],
      providers: [
        provideRouter([]),
        { provide: WalletService, useValue: walletServiceMock }
      ]
    }).compileComponents();
  });

  // F-04: Có trạng thái rỗng khi chưa có ví. Hiện tổng tài sản. Khi excludedWallets không rỗng thì hiện cảnh báo.
  it('F-04: should show empty state when wallets list is empty', () => {
    fixture = TestBed.createComponent(WalletListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    const emptyState = compiled.querySelector('.empty-state');
    expect(emptyState).toBeTruthy();
    expect(emptyState?.textContent).toContain('Chưa có ví nào');
  });

  it('F-04: should display total assets and warning when excludedWallets is not empty', () => {
    const mockWallets: WalletDto[] = [
      {
        id: 'w1',
        userId: 'u1',
        name: 'Ví VND',
        type: 1,
        balance: 10_000_000,
        initialBalance: 10_000_000,
        currency: 'VND',
        isDefault: true,
        isArchived: false,
        createdAt: '2026-11-22T00:00:00Z',
        updatedAt: '2026-11-22T00:00:00Z'
      },
      {
        id: 'w2',
        userId: 'u1',
        name: 'Ví USD',
        type: 2,
        balance: 500,
        initialBalance: 500,
        currency: 'USD',
        isDefault: false,
        isArchived: false,
        createdAt: '2026-11-22T00:00:00Z',
        updatedAt: '2026-11-22T00:00:00Z'
      }
    ];

    const mockTotalAssets: TotalAssetsResponse = {
      totalAssetsVnd: 10_000_000,
      ratesUnavailable: true,
      excludedWallets: [
        { id: 'w2', name: 'Ví USD', currency: 'USD', balance: 500 }
      ]
    };

    walletServiceMock.getWallets.mockReturnValue(of(mockWallets));
    walletServiceMock.getTotalAssets.mockReturnValue(of(mockTotalAssets));

    fixture = TestBed.createComponent(WalletListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;

    // Total assets display
    const totalAssetsEl = compiled.querySelector('.total-assets');
    expect(totalAssetsEl).toBeTruthy();
    expect(totalAssetsEl?.textContent).toContain('10,000,000');

    // Warning banner for excluded wallets
    const warningEl = compiled.querySelector('.excluded-warning');
    expect(warningEl).toBeTruthy();
    expect(warningEl?.textContent).toContain('Có 1 ví ngoại tệ chưa được quy đổi');
    expect(warningEl?.textContent).toContain('Ví USD');
  });

  // F-07: Danh sách ví không bị tràn ngang ở độ rộng 360px
  it('F-07: should have responsive styling and not overflow at 360px width', () => {
    fixture = TestBed.createComponent(WalletListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    const container = compiled.querySelector('.wallet-list-container') as HTMLElement;
    expect(container).toBeTruthy();
    expect(container.style.maxWidth).toBe('100%');
    expect(container.style.overflowX).toBe('hidden');
    expect(container.style.boxSizing).toBe('border-box');
  });
});

