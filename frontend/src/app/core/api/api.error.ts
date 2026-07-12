export class ApiError extends Error {
    readonly status: number | null;
    readonly code?: string;
    readonly errors?: Record<string, string[]>;

    constructor(message: string, status: number | null, code?: string, errors?: Record<string, string[]>) {
        super(message);

        this.name = 'ApiError';
        this.status = status;
        this.code = code;
        this.errors = errors;
    }
}
