using FoodDelivery.Domain.Common;

namespace FoodDelivery.Domain.Tenancy;

public sealed record TenantStatusChangedDomainEvent(Guid TenantId, TenantStatus Status) : IDomainEvent;
