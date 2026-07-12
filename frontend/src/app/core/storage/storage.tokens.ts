import { InjectionToken } from '@angular/core';

export interface StorageConfig {
    prefix: string;
    version: number;
}

export const STORAGE_CONFIG = new InjectionToken<StorageConfig>('STORAGE_CONFIG', {
    providedIn: 'root',
    factory: () => ({
        prefix: 'my-app',
        version: 1,
    }),
});
