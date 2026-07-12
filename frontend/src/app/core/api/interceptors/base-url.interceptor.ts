import { inject } from '@angular/core';
import { HttpInterceptorFn } from '@angular/common/http';

import { API_CONFIG } from '../api.tokens';

const ABSOLUTE_URL_PATTERN = /^https?:\/\//;

export const baseUrlInterceptor: HttpInterceptorFn = (req, next) => {
    if (ABSOLUTE_URL_PATTERN.test(req.url)) {
        return next(req);
    }

    const config = inject(API_CONFIG);

    return next(req.clone({ url: `${config.baseUrl}${req.url}` }));
};
