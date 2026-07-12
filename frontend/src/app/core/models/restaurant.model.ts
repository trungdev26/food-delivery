export interface Restaurant {
    id: string;
    name: string;
    image: string;
    cuisine: string;
    rating: number;
    reviewCount: number;
    deliveryTimeMin: number;
    deliveryFee: number;
    distanceKm: number;
    address: string;
    tags: string[];
    promo?: string;
}
