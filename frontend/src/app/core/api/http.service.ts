import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable, TimeoutError, catchError, throwError, timeout } from 'rxjs';

import { ApiError } from './api.error';
import { API_CONFIG } from './api.tokens';
import { ApiErrorPayload, ApiRequestOptions } from './api.types';

@Injectable({
    providedIn: 'root',
})
export class HttpService {
    private readonly http = inject(HttpClient);
    private readonly config = inject(API_CONFIG);

    get<T>(url: string, options?: ApiRequestOptions): Observable<T> {
        return this.request<T>('GET', url, undefined, options);
    }

    post<T>(url: string, body?: unknown, options?: ApiRequestOptions): Observable<T> {
        return this.request<T>('POST', url, body, options);
    }

    put<T>(url: string, body?: unknown, options?: ApiRequestOptions): Observable<T> {
        return this.request<T>('PUT', url, body, options);
    }

    patch<T>(url: string, body?: unknown, options?: ApiRequestOptions): Observable<T> {
        return this.request<T>('PATCH', url, body, options);
    }

    delete<T>(url: string, options?: ApiRequestOptions): Observable<T> {
        return this.request<T>('DELETE', url, undefined, options);
    }

    private request<T>(method: string, url: string, body: unknown, options?: ApiRequestOptions): Observable<T> {
        return this.http
            .request<T>(method, url, {
                body,
                params: options?.params,
                headers: options?.headers,
            })
            .pipe(
                timeout(this.config.timeout),
                catchError((error: unknown) => throwError(() => this.toApiError(error))),
            );
    }

    private toApiError(error: unknown): ApiError {
        if (error instanceof TimeoutError) {
            return new ApiError('Request quá thời gian chờ', null, 'ERR_TIMEOUT');
        }

        if (error instanceof HttpErrorResponse) {
            const payload = error.error as ApiErrorPayload | null;

            return new ApiError(
                payload?.message ?? error.message,
                error.status || null,
                payload?.code,
                payload?.errors,
            );
        }

        return new ApiError('Đã có lỗi không xác định xảy ra', null);
    }
}
