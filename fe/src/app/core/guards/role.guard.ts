import { CanActivateFn, Router } from '@angular/router';
import { inject } from '@angular/core';
import { AuthService } from '../services/auth.service';

export const roleGuard = (expectedRoles: string[]): CanActivateFn => {
  return () => {
    const authService = inject(AuthService);
    const router = inject(Router);

    if (!authService.isAuthenticated()) {
      router.navigate(['/login']);
      return false;
    }

    const currentRole = authService.getUserRole();
    const roles = authService.currentUserRoles();
    if ((currentRole && expectedRoles.includes(currentRole)) || roles.some(r => expectedRoles.includes(r))) {
      return true;
    }

    router.navigate(['/wallets']);
    return false;
  };
};

export const permissionGuard = (requiredPermissions: string[]): CanActivateFn => {
  return () => {
    const authService = inject(AuthService);
    const router = inject(Router);

    if (!authService.isAuthenticated()) {
      router.navigate(['/login']);
      return false;
    }

    if (requiredPermissions.some(p => authService.hasPermission(p))) {
      return true;
    }

    router.navigate(['/wallets']);
    return false;
  };
};
