export type StorageType = 'local' | 'session';

export interface StorageOptions {
    // Storage type: mặc định 'local'
    storage?: StorageType;
    ttl?: number;
}

export interface StorageEntry<T> {
    value: T;

    /**
     * Thời điểm hết hạn theo Unix timestamp milliseconds.
     * null nghĩa là không hết hạn.
     */
    expiresAt: number | null;

    /**
     * Version schema của dữ liệu.
     * Có thể dùng để migration sau này.
     */
    version: number;
}
export interface StorageResult<T> {
    value: T | null;
    found: boolean;
    expired: boolean;
}
