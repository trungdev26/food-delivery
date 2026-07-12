export interface ApiRequestOptions {
    params?: Record<string, string | number | boolean>;
    headers?: Record<string, string>;
}

export interface ApiErrorPayload {
    message: string;
    code?: string;
    errors?: Record<string, string[]>;
}
