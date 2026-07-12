import { DecimalPipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Food } from '../../core/models/food.model';
import { FOODS, RESTAURANTS } from '../../core/mock/mock-data';
import { QuantityStepperComponent } from '../../shared/components/quantity-stepper/quantity-stepper.component';
import { StarRatingComponent } from '../../shared/components/star-rating/star-rating.component';

@Component({
    selector: 'app-restaurant-detail',
    imports: [RouterLink, StarRatingComponent, QuantityStepperComponent, DecimalPipe],
    templateUrl: './restaurant-detail.component.html',
})
export class RestaurantDetailComponent {
    private readonly route = inject(ActivatedRoute);

    readonly restaurant = RESTAURANTS.find(
        (item) => item.id === this.route.snapshot.paramMap.get('id'),
    );

    readonly menuByCategory = computed(() => {
        const foods = FOODS.filter((food) => food.restaurantId === this.restaurant?.id);
        const categories = [...new Set(foods.map((food) => food.category))];

        return categories.map((category) => ({
            category,
            foods: foods.filter((food) => food.category === category),
        }));
    });

    // Trạng thái giỏ hàng tạm thời để demo giao diện, sẽ thay bằng service thật sau.
    private readonly quantities = signal<Record<string, number>>({});

    readonly totalQuantity = computed(() =>
        Object.values(this.quantities()).reduce((sum, qty) => sum + qty, 0),
    );

    readonly totalPrice = computed(() => {
        const quantities = this.quantities();
        return FOODS.reduce((sum, food) => sum + (quantities[food.id] ?? 0) * food.price, 0);
    });

    quantityOf(food: Food): number {
        return this.quantities()[food.id] ?? 0;
    }

    increase(food: Food): void {
        this.quantities.update((current) => ({
            ...current,
            [food.id]: (current[food.id] ?? 0) + 1,
        }));
    }

    decrease(food: Food): void {
        this.quantities.update((current) => {
            const nextQty = (current[food.id] ?? 0) - 1;
            const { [food.id]: _removed, ...rest } = current;
            return nextQty > 0 ? { ...rest, [food.id]: nextQty } : rest;
        });
    }
}
