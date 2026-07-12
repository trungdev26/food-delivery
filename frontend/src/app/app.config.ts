import { registerLocaleData } from '@angular/common';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import en from '@angular/common/locales/en';
import { ApplicationConfig, provideZoneChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';

import { routes } from './app.routes';
import { authInterceptor } from './core/api/interceptors/auth.interceptor';
import { baseUrlInterceptor } from './core/api/interceptors/base-url.interceptor';
import { unauthorizedInterceptor } from './core/api/interceptors/unauthorized.interceptor';

registerLocaleData(en);

export const appConfig: ApplicationConfig = {
    providers: [
        provideZoneChangeDetection({ eventCoalescing: true }),
        provideRouter(routes),
        provideHttpClient(withInterceptors([baseUrlInterceptor, authInterceptor, unauthorizedInterceptor])),
    ],
};
