import { inject, Injectable, PLATFORM_ID } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';

import { StorageEntry, StorageOptions, StorageResult, StorageType } from './storage.types';
import { STORAGE_CONFIG, StorageConfig } from './storage.tokens';

@Injectable({
    providedIn: 'root',
})
export class BrowserStorageService {
    private readonly platformId = inject(PLATFORM_ID);
    private readonly config = inject(STORAGE_CONFIG);

    private readonly isBrowser = isPlatformBrowser(this.platformId);

    set<T>(key: string, value: T, options: StorageOptions = {}): boolean {
        const storage = this.getStorage(options.storage);

        if (!storage) {
            return false;
        }

        const entry: StorageEntry<T> = {
            value,
            expiresAt: options.ttl ? Date.now() + options.ttl : null,
            version: this.config.version,
        };

        try {
            storage.setItem(this.buildKey(key), JSON.stringify(entry));

            return true;
        } catch (error) {
            this.handleStorageError('set', key, error);
            return false;
        }
    }

    get<T>(key: string, storageType: StorageType = 'local'): T | null {
        return this.getResult<T>(key, storageType).value;
    }

    getResult<T>(key: string, storageType: StorageType = 'local'): StorageResult<T> {
        const storage = this.getStorage(storageType);

        if (!storage) {
            return {
                value: null,
                found: false,
                expired: false,
            };
        }

        const fullKey = this.buildKey(key);

        try {
            const rawValue = storage.getItem(fullKey);

            if (rawValue === null) {
                return {
                    value: null,
                    found: false,
                    expired: false,
                };
            }

            const entry = JSON.parse(rawValue) as StorageEntry<T>;

            if (!this.isValidEntry(entry)) {
                storage.removeItem(fullKey);

                return {
                    value: null,
                    found: false,
                    expired: false,
                };
            }

            if (this.isExpired(entry)) {
                storage.removeItem(fullKey);

                return {
                    value: null,
                    found: true,
                    expired: true,
                };
            }

            return {
                value: entry.value,
                found: true,
                expired: false,
            };
        } catch (error) {
            /*
             * Dữ liệu có thể bị sửa thủ công,
             * schema cũ hoặc JSON không hợp lệ.
             */
            this.safeRemove(storage, fullKey);
            this.handleStorageError('get', key, error);

            return {
                value: null,
                found: false,
                expired: false,
            };
        }
    }

    has(key: string, storageType: StorageType = 'local'): boolean {
        return this.getResult(key, storageType).value !== null;
    }

    remove(key: string, storageType: StorageType = 'local'): boolean {
        const storage = this.getStorage(storageType);

        if (!storage) {
            return false;
        }

        try {
            storage.removeItem(this.buildKey(key));
            return true;
        } catch (error) {
            this.handleStorageError('remove', key, error);
            return false;
        }
    }

    clear(storageType: StorageType = 'local'): void {
        const storage = this.getStorage(storageType);

        if (!storage) {
            return;
        }

        const prefix = `${this.config.prefix}:`;
        const keysToRemove: string[] = [];

        try {
            for (let index = 0; index < storage.length; index++) {
                const key = storage.key(index);

                if (key?.startsWith(prefix)) {
                    keysToRemove.push(key);
                }
            }

            /*
             * Không remove trong lúc đang iterate vì storage.length
             * và index sẽ thay đổi.
             */
            keysToRemove.forEach((key) => storage.removeItem(key));
        } catch (error) {
            this.handleStorageError('clear', prefix, error);
        }
    }

    clearAll(): void {
        this.clear('local');
        this.clear('session');
    }

    private getStorage(storageType: StorageType = 'local'): Storage | null {
        if (!this.isBrowser) {
            return null;
        }

        try {
            return storageType === 'session' ? window.sessionStorage : window.localStorage;
        } catch {
            /*
             * Một số browser mode hoặc privacy policy có thể
             * chặn quyền truy cập storage.
             */
            return null;
        }
    }

    private buildKey(key: string): string {
        return `${this.config.prefix}:${key}`;
    }

    private isExpired<T>(entry: StorageEntry<T>): boolean {
        return entry.expiresAt !== null && Date.now() >= entry.expiresAt;
    }

    private isValidEntry<T>(entry: unknown): entry is StorageEntry<T> {
        if (typeof entry !== 'object' || entry === null) {
            return false;
        }

        const candidate = entry as Partial<StorageEntry<T>>;

        return (
            'value' in candidate &&
            (candidate.expiresAt === null || typeof candidate.expiresAt === 'number') &&
            typeof candidate.version === 'number'
        );
    }

    private safeRemove(storage: Storage, fullKey: string): void {
        try {
            storage.removeItem(fullKey);
        } catch {
            // Không làm gián đoạn application flow.
        }
    }

    private handleStorageError(operation: string, key: string, error: unknown): void {
        /*
         * Production thực tế nên đẩy sang LoggerService,
         * Sentry hoặc hệ thống monitoring.
         */
        console.warn(`[Storage] Cannot ${operation} key "${key}"`, error);
    }
}
