import { Injectable } from '@angular/core';
import { LoginRequest } from './auth.types';

@Injectable({ providedIn: 'root' })
export class AuthService {
    login(request: LoginRequest) {}

    logout() {}

    isLoggedIn(): boolean {
        return true; 
    }
}
