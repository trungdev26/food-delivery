import { inject } from '@angular/core';
import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';

import { AppStorageService } from '../../storage/app-storage.service';

export const unauthorizedInterceptor: HttpInterceptorFn = (req, next) => {
    const storage = inject(AppStorageService);
    const router = inject(Router);

    return next(req).pipe(
        catchError((error: unknown) => {
            if (error instanceof HttpErrorResponse && error.status === 401) {
                storage.clearAuthentication();
                router.navigate(['/auth/login']);
            }

            return throwError(() => error);
        }),
    );
};
