import { DecimalPipe } from '@angular/common';
import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Restaurant } from '../../../core/models/restaurant.model';
import { StarRatingComponent } from '../star-rating/star-rating.component';

@Component({
    selector: 'app-restaurant-card',
    imports: [RouterLink, StarRatingComponent, DecimalPipe],
    template: `
        <a
            [routerLink]="['/restaurant', restaurant().id]"
            class="group block overflow-hidden rounded-2xl bg-white shadow-sm ring-1 ring-neutral-100 transition hover:shadow-md"
        >
            <div class="relative aspect-[4/3] overflow-hidden bg-neutral-100">
                <img
                    [src]="restaurant().image"
                    [alt]="restaurant().name"
                    loading="lazy"
                    decoding="async"
                    class="h-full w-full object-cover transition duration-300 group-hover:scale-105"
                />
                @if (restaurant().promo) {
                    <span
                        class="absolute left-2 top-2 rounded-full bg-emerald-600 px-2.5 py-1 text-xs font-semibold text-white shadow"
                    >
                        {{ restaurant().promo }}
                    </span>
                }
            </div>

            <div class="space-y-1.5 p-3">
                <h3 class="truncate font-semibold text-neutral-900">{{ restaurant().name }}</h3>

                <div class="flex items-center gap-2 text-sm text-neutral-500">
                    <app-star-rating
                        [rating]="restaurant().rating"
                        [reviewCount]="restaurant().reviewCount"
                    />
                    <span>·</span>
                    <span>{{ restaurant().cuisine }}</span>
                </div>

                <div class="flex items-center gap-3 text-xs text-neutral-500">
                    <span>⏱ {{ restaurant().deliveryTimeMin }} phút</span>
                    <span>📍 {{ restaurant().distanceKm }} km</span>
                    <span>🚚 {{ restaurant().deliveryFee | number }}đ</span>
                </div>
            </div>
        </a>
    `,
})
export class RestaurantCardComponent {
    restaurant = input.required<Restaurant>();
}
