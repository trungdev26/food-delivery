import { Component, input } from '@angular/core';

@Component({
    selector: 'app-star-rating',
    template: `
        <span class="inline-flex items-center gap-1 text-sm font-medium text-amber-600">
            <svg class="h-4 w-4 fill-amber-500" viewBox="0 0 20 20">
                <path
                    d="M10 1.6l2.59 5.25 5.79.84-4.19 4.09.99 5.77L10 14.77l-5.18 2.78.99-5.77L1.62 7.7l5.79-.84L10 1.6z"
                />
            </svg>
            {{ rating().toFixed(1) }}
            @if (reviewCount() !== undefined) {
                <span class="font-normal text-neutral-400">({{ reviewCount() }})</span>
            }
        </span>
    `,
})
export class StarRatingComponent {
    rating = input.required<number>();
    reviewCount = input<number>();
}
