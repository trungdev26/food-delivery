import { DecimalPipe } from '@angular/common';
import { Component, computed, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CATEGORIES, FOODS, RESTAURANTS } from '../../core/mock/mock-data';
import { RestaurantCardComponent } from '../../shared/components/restaurant-card/restaurant-card.component';

@Component({
    selector: 'app-home',
    imports: [RestaurantCardComponent, RouterLink, DecimalPipe],
    templateUrl: './home.component.html',
})
export class HomeComponent {
    readonly categories = CATEGORIES;
    readonly popularFoods = FOODS.filter((food) => food.isPopular);

    readonly selectedCategoryId = signal<string | null>(null);

    readonly filteredRestaurants = computed(() => {
        const categoryId = this.selectedCategoryId();
        if (!categoryId) {
            return RESTAURANTS;
        }

        const category = CATEGORIES.find((item) => item.id === categoryId);
        return RESTAURANTS.filter((restaurant) => restaurant.cuisine === category?.name);
    });

    selectCategory(categoryId: string): void {
        this.selectedCategoryId.set(this.selectedCategoryId() === categoryId ? null : categoryId);
    }

    restaurantName(restaurantId: string): string {
        return RESTAURANTS.find((restaurant) => restaurant.id === restaurantId)?.name ?? '';
    }
}
