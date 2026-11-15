import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { LoginComponent } from './login.component';
import { AuthService } from '../../../core/services/auth.service';

class DummyProfileComponent {}

describe('LoginComponent', () => {
  let authServiceMock: Partial<AuthService>;

  beforeEach(async () => {
    authServiceMock = {
      login: vi.fn().mockReturnValue(of({
        accessToken: 'fake-token',
        refreshToken: 'fake-refresh',
        expiresIn: 1800,
        user: { id: 'u1', email: 'test@example.com', roles: ['User'] }
      }))
    };

    await TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        provideRouter([{ path: 'profile', component: DummyProfileComponent }]),
        { provide: AuthService, useValue: authServiceMock }
      ]
    }).compileComponents();
  });

  it('should create LoginComponent and initialize form', () => {
    const fixture = TestBed.createComponent(LoginComponent);
    const component = fixture.componentInstance;
    expect(component).toBeTruthy();
    expect(component.loginForm.valid).toBeFalsy();
    expect(component.isLoading()).toBe(false);
  });

  it('should validate email format and required password', () => {
    const fixture = TestBed.createComponent(LoginComponent);
    const component = fixture.componentInstance;

    const emailCtrl = component.loginForm.controls['email'];
    const passCtrl = component.loginForm.controls['password'];

    emailCtrl.setValue('invalid-email');
    passCtrl.setValue('');
    expect(emailCtrl.valid).toBeFalsy();
    expect(passCtrl.valid).toBeFalsy();

    emailCtrl.setValue('valid@example.com');
    passCtrl.setValue('SecurePassword123');
    expect(component.loginForm.valid).toBeTruthy();
  });

  it('should call authService.login on valid submit', () => {
    const fixture = TestBed.createComponent(LoginComponent);
    const component = fixture.componentInstance;

    component.loginForm.setValue({
      email: 'user@example.com',
      password: 'Password123'
    });

    component.onSubmit();
    expect(authServiceMock.login).toHaveBeenCalledWith({
      email: 'user@example.com',
      password: 'Password123'
    });
    expect(component.isLoading()).toBe(false);
  });

  it('should display lockout message on 423 status', () => {
    (authServiceMock.login as any).mockReturnValue(
      throwError(() => ({
        status: 423,
        error: { message: 'Tài khoản bị khóa 15 phút' }
      }))
    );

    const fixture = TestBed.createComponent(LoginComponent);
    const component = fixture.componentInstance;

    component.loginForm.setValue({
      email: 'locked@example.com',
      password: 'Password123'
    });

    component.onSubmit();
    expect(component.lockoutMessage()).toContain('Tài khoản bị khóa 15 phút');
    expect(component.isLoading()).toBe(false);
  });
});
