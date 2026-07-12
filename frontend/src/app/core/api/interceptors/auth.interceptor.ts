import { inject } from '@angular/core';
import { HttpInterceptorFn } from '@angular/common/http';

import { AppStorageService } from '../../storage/app-storage.service';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
    const storage = inject(AppStorageService);
    const token = storage.getAccessToken();

    if (!token) {
        return next(req);
    }

    return next(
        req.clone({
            setHeaders: { Authorization: `Bearer ${token}` },
        }),
    );
};
