import { inject, Injectable } from '@angular/core';

import { BrowserStorageService } from './browser-storage.service';
import { STORAGE_KEYS } from './storage-keys';

export interface CurrentUser {
    id: number;
    username: string;
    displayName: string;
    permissions: string[];
}

export interface OrderFilter {
    keyword: string;
    status: string | null;
    fromDate: string | null;
    toDate: string | null;
}

@Injectable({
    providedIn: 'root',
})
export class AppStorageService {
    private readonly storage = inject(BrowserStorageService);

    setAccessToken(token: string): void {
        this.storage.set(STORAGE_KEYS.auth.accessToken, token, {
            storage: 'session',
            ttl: 30 * 60 * 1000,
        });
    }

    getAccessToken(): string | null {
        return this.storage.get<string>(STORAGE_KEYS.auth.accessToken, 'session');
    }

    removeAccessToken(): void {
        this.storage.remove(STORAGE_KEYS.auth.accessToken, 'session');
    }

    setCurrentUser(user: CurrentUser): void {
        this.storage.set(STORAGE_KEYS.auth.currentUser, user, {
            storage: 'session',
            ttl: 30 * 60 * 1000,
        });
    }

    getCurrentUser(): CurrentUser | null {
        return this.storage.get<CurrentUser>(STORAGE_KEYS.auth.currentUser, 'session');
    }

    setOrderFilter(filter: OrderFilter): void {
        this.storage.set(STORAGE_KEYS.features.orderFilter, filter, {
            storage: 'local',
            ttl: 7 * 24 * 60 * 60 * 1000,
        });
    }

    getOrderFilter(): OrderFilter | null {
        return this.storage.get<OrderFilter>(STORAGE_KEYS.features.orderFilter);
    }

    clearAuthentication(): void {
        this.storage.remove(STORAGE_KEYS.auth.accessToken, 'session');

        this.storage.remove(STORAGE_KEYS.auth.refreshToken, 'session');

        this.storage.remove(STORAGE_KEYS.auth.currentUser, 'session');
    }

    clearApplicationData(): void {
        this.storage.clearAll();
    }
}
