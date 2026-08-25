using System.Text.RegularExpressions;
using FoodDelivery.Domain.Common;

namespace FoodDelivery.Domain.Tenancy;

public sealed class Tenant : AggregateRoot<Guid>
{
    private static readonly Regex ValidSubdomain = new(
        "^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$",
        RegexOptions.Compiled);

    private Tenant(Guid id, string name, string subdomain) : base(id)
    {
        Name = name;
        Subdomain = subdomain;
        Status = TenantStatus.Active;
    }

    public string Name { get; private set; }
    public string Subdomain { get; private set; }
    public TenantStatus Status { get; private set; }

    public static Tenant Create(Guid id, string name, string subdomain)
    {
        var normalized = subdomain.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tenant name is required.", nameof(name));
        if (!ValidSubdomain.IsMatch(normalized))
            throw new ArgumentException("Invalid subdomain.", nameof(subdomain));

        return new Tenant(id, name.Trim(), normalized);
    }

    public void Activate() => ChangeStatus(TenantStatus.Active);
    public void Deactivate() => ChangeStatus(TenantStatus.Inactive);

    private void ChangeStatus(TenantStatus status)
    {
        if (Status == status) return;
        Status = status;
        RaiseDomainEvent(new TenantStatusChangedDomainEvent(Id, status));
    }
}
