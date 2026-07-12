import { Routes } from '@angular/router';
import { MainLayoutComponent } from './layout/main-layout/main-layout.component';

export const routes: Routes = [
    {
        path: '',
        component: MainLayoutComponent,
        children: [
            {
                path: '',
                loadComponent: () =>
                    import('./features/home/home.component').then((m) => m.HomeComponent),
            },
            {
                path: 'restaurant/:id',
                loadComponent: () =>
                    import('./features/restaurant-detail/restaurant-detail.component').then(
                        (m) => m.RestaurantDetailComponent,
                    ),
            },
        ],
    },
];
