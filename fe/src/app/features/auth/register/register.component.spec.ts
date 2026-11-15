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
});
