import { Component, input, output } from '@angular/core';

@Component({
    selector: 'app-quantity-stepper',
    template: `
        @if (quantity() > 0) {
            <div
                class="flex items-center gap-3 rounded-full bg-emerald-600 px-1 py-1 text-white transition-all"
            >
                <button
                    type="button"
                    class="flex h-7 w-7 items-center justify-center rounded-full text-lg font-semibold transition-transform hover:bg-emerald-700 active:scale-90"
                    (click)="decrease.emit()"
                >
                    −
                </button>
                <span class="w-4 text-center text-sm font-semibold tabular-nums">{{ quantity() }}</span>
                <button
                    type="button"
                    class="flex h-7 w-7 items-center justify-center rounded-full text-lg font-semibold transition-transform hover:bg-emerald-700 active:scale-90"
                    (click)="increase.emit()"
                >
                    +
                </button>
            </div>
        } @else {
            <button
                type="button"
                class="rounded-full bg-emerald-600 px-4 py-1.5 text-sm font-semibold text-white transition-transform hover:bg-emerald-700 active:scale-90"
                (click)="increase.emit()"
            >
                Thêm
            </button>
        }
    `,
})
export class QuantityStepperComponent {
    quantity = input(0);
    increase = output<void>();
    decrease = output<void>();
}
