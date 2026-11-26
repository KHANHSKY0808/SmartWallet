import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { RegisterComponent } from './register.component';
import { AuthService } from '../../../core/services/auth.service';

describe('RegisterComponent', () => {
  let authServiceMock: Partial<AuthService>;

  beforeEach(async () => {
    authServiceMock = {
      register: vi.fn().mockReturnValue(of({
        message: 'Đăng ký thành công!',
        userId: 'u-123'
      })),
      resendConfirmation: vi.fn().mockReturnValue(of({
        message: 'Nếu email tồn tại và chưa được kích hoạt, hệ thống đã gửi lại email xác nhận.'
      }))
    };

    await TestBed.configureTestingModule({
      imports: [RegisterComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: authServiceMock }
      ]
    }).compileComponents();
  });

  it('should create RegisterComponent', () => {
    const fixture = TestBed.createComponent(RegisterComponent);
    const component = fixture.componentInstance;
    expect(component).toBeTruthy();
    expect(component.registerForm.valid).toBeFalsy();
  });

  it('should validate password mismatch', () => {
    const fixture = TestBed.createComponent(RegisterComponent);
    const component = fixture.componentInstance;

    component.registerForm.patchValue({
      email: 'test@example.com',
      password: 'Password123',
      confirmPassword: 'MismatchPassword'
    });

    expect(component.registerForm.hasError('passwordMismatch')).toBe(true);
    expect(component.registerForm.valid).toBe(false);

    component.registerForm.patchValue({
      confirmPassword: 'Password123'
    });
    expect(component.registerForm.hasError('passwordMismatch')).toBe(false);
    expect(component.registerForm.valid).toBe(true);
  });

  it('should call authService.register and set successMessage', () => {
    const fixture = TestBed.createComponent(RegisterComponent);
    const component = fixture.componentInstance;

    component.registerForm.setValue({
      fullName: 'Nguyen Van A',
      email: 'newuser@example.com',
      password: 'Password123',
      confirmPassword: 'Password123'
    });

    component.onSubmit();
    expect(authServiceMock.register).toHaveBeenCalled();
    expect(component.successMessage()).toContain('Đăng ký thành công');
    expect(component.isLoading()).toBe(false);
  });

  it('should handle conflict email error (409)', () => {
    (authServiceMock.register as any).mockReturnValue(
      throwError(() => ({
        status: 409,
        error: { message: 'Email đã được sử dụng' }
      }))
    );

    const fixture = TestBed.createComponent(RegisterComponent);
    const component = fixture.componentInstance;

    component.registerForm.setValue({
      fullName: 'Duplicate User',
      email: 'existing@example.com',
      password: 'Password123',
      confirmPassword: 'Password123'
    });

    component.onSubmit();
    expect(component.errorMessage()).toContain('Email đã được sử dụng');
    expect(component.isLoading()).toBe(false);
  });

  it('D67/N44: should render resend button after registration and call resendConfirmation', () => {
    const fixture = TestBed.createComponent(RegisterComponent);
    const component = fixture.componentInstance;

    component.registerForm.setValue({
      fullName: 'Test Resend',
      email: 'resend@example.com',
      password: 'Password123',
      confirmPassword: 'Password123'
    });

    component.onSubmit();
    fixture.detectChanges();

    const resendBtn = (fixture.nativeElement as HTMLElement).querySelector('.btn-resend') as HTMLButtonElement;
    expect(resendBtn).toBeTruthy();
    expect(resendBtn.textContent).toContain('Gửi lại email xác nhận');

    resendBtn.click();
    expect(authServiceMock.resendConfirmation).toHaveBeenCalledWith('resend@example.com');
    expect(component.resendMessage()).toContain('gửi lại email xác nhận');
  });
});

