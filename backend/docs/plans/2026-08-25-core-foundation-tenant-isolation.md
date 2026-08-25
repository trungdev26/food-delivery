# Core Foundation and Tenant Isolation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Chuyển solution hiện tại thành nền DDD Modular Monolith có dependency đúng chiều, Domain Event in-process và Tenant isolation dùng được cho các module Phase 1.

**Architecture:** Giữ bốn project hiện có. `Domain` chứa model thuần; `Application` chứa contracts/use cases; `Infrastructure` triển khai EF Core và tenant persistence; `Api` resolve tenant từ authenticated user hoặc subdomain và là composition root.

**Tech Stack:** .NET 6, ASP.NET Core, EF Core 6.0.36, Pomelo MySQL 6.0.2, xUnit.

**Spec:** `backend/docs/architecture/ddd-event-architecture-phase-1.md`

## Global Constraints

- Giữ tên kỹ thuật bằng English và tên nghiệp vụ bằng tiếng Việt không dấu.
- Giữ `Tenant`, `Shop`; không thêm hậu tố `Entity` vào Domain type.
- Không thêm MediatR, generic repository, generic service hoặc message broker.
- Mọi dữ liệu tenant-owned phải có `TenantId`; command ghi dữ liệu phải kiểm tra ownership.
- Domain không phụ thuộc EF Core, HTTP hoặc Infrastructure.
- Không sửa/xóa các thay đổi không thuộc plan đang có trong worktree.
- Trước khi sửa file đã modified/untracked, đọc `git diff` và nội dung hiện tại; khi commit dùng `git add -p` cho file có thay đổi từ trước để không cuốn code của người dùng vào commit.

## Roadmap Phase 1

Plan này là chặng 1 trong bốn chặng độc lập theo dependency:

1. **Core Foundation + Tenant Isolation** — plan hiện tại.
2. **Shop + Catalog + đồng bộ `ThucDonShop`** — dựa trên contracts và tenant boundary từ chặng 1.
3. **Ordering** — tạo `DonHang`, snapshot giá, state transition và Domain Events.
4. **Angular flows + end-to-end verification** — Tenant Admin, Shop Staff và Customer.

---

### Task 1: Chuẩn hóa project dependency

**Files:**
- Modify: `backend/FoodDelivery.Domain/FoodDelivery.Domain.csproj`
- Modify: `backend/FoodDelivery.Application/FoodDelivery.Application.csproj`
- Modify: `backend/FoodDelivery.Infrastructure/FoodDelivery.Infrastructure.csproj`
- Modify: `backend/FoodDelivery.Api/FoodDelivery.Api.csproj`
- Delete: `backend/FoodDelivery.Domain/Program.cs`
- Delete: `backend/FoodDelivery.Application/Program.cs`
- Delete: `backend/FoodDelivery.Infrastructure/Program.cs`
- Create: `backend/FoodDelivery.Domain.Tests/FoodDelivery.Domain.Tests.csproj`
- Modify: `backend/FoodDelivery.slnx`

**Interfaces:**
- Produces: buildable class libraries `Domain`, `Application`, `Infrastructure`; xUnit test project referencing `Domain`.

- [ ] **Step 1: Chuyển ba project placeholder thành class library**

Xóa `<OutputType>Exe</OutputType>` và giữ `TargetFramework`, `ImplicitUsings`, `Nullable` trong cả ba `.csproj`; xóa ba `Program.cs` placeholder.

- [ ] **Step 2: Khai báo project references đúng chiều**

`FoodDelivery.Application.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="../FoodDelivery.Domain/FoodDelivery.Domain.csproj" />
</ItemGroup>
```

`FoodDelivery.Infrastructure.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="../FoodDelivery.Application/FoodDelivery.Application.csproj" />
  <ProjectReference Include="../FoodDelivery.Domain/FoodDelivery.Domain.csproj" />
</ItemGroup>
<ItemGroup>
  <PackageReference Include="Pomelo.EntityFrameworkCore.MySql" Version="6.0.2" />
</ItemGroup>
```

`FoodDelivery.Api.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="../FoodDelivery.Application/FoodDelivery.Application.csproj" />
  <ProjectReference Include="../FoodDelivery.Domain/FoodDelivery.Domain.csproj" />
  <ProjectReference Include="../FoodDelivery.Infrastructure/FoodDelivery.Infrastructure.csproj" />
</ItemGroup>
```

- [ ] **Step 3: Tạo test project tối thiểu**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.6.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.4">
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../FoodDelivery.Domain/FoodDelivery.Domain.csproj" />
  </ItemGroup>
</Project>
```

Thêm project vào `FoodDelivery.slnx`.

- [ ] **Step 4: Verify dependency baseline**

Run: `dotnet build backend/FoodDelivery.slnx`

Expected: build thành công; không còn lỗi executable entry point trong ba class library.

- [ ] **Step 5: Commit**

```bash
git add backend/FoodDelivery.slnx backend/FoodDelivery.Api/FoodDelivery.Api.csproj backend/FoodDelivery.Application backend/FoodDelivery.Domain backend/FoodDelivery.Infrastructure backend/FoodDelivery.Domain.Tests
git commit -m "build: establish DDD project dependencies"
```

### Task 2: Domain primitives và Domain Event collection

**Files:**
- Create: `backend/FoodDelivery.Domain/Common/IDomainEvent.cs`
- Create: `backend/FoodDelivery.Domain/Common/Entity.cs`
- Create: `backend/FoodDelivery.Domain/Common/AggregateRoot.cs`
- Create: `backend/FoodDelivery.Domain.Tests/Common/AggregateRootTests.cs`

**Interfaces:**
- Produces: `IDomainEvent`, `Entity<TId>`, `AggregateRoot<TId>.DomainEvents`, `RaiseDomainEvent`, `ClearDomainEvents`.

- [ ] **Step 1: Viết failing test cho event collection**

```csharp
using FoodDelivery.Domain.Common;

namespace FoodDelivery.Domain.Tests.Common;

public class AggregateRootTests
{
    private sealed record SomethingHappened : IDomainEvent;
    private sealed class TestAggregate : AggregateRoot<Guid>
    {
        public TestAggregate() : base(Guid.NewGuid()) { }
        public void Change() => RaiseDomainEvent(new SomethingHappened());
    }

    [Fact]
    public void Raise_and_clear_domain_events()
    {
        var aggregate = new TestAggregate();
        aggregate.Change();

        Assert.Single(aggregate.DomainEvents);
        aggregate.ClearDomainEvents();
        Assert.Empty(aggregate.DomainEvents);
    }
}
```

- [ ] **Step 2: Run test và xác nhận RED**

Run: `dotnet test backend/FoodDelivery.Domain.Tests --filter Raise_and_clear_domain_events`

Expected: FAIL vì `FoodDelivery.Domain.Common` chưa tồn tại.

- [ ] **Step 3: Implement primitives tối thiểu**

```csharp
namespace FoodDelivery.Domain.Common;

public interface IDomainEvent { }

public abstract class Entity<TId>
{
    protected Entity(TId id) => Id = id;
    public TId Id { get; protected set; }
}

public abstract class AggregateRoot<TId> : Entity<TId>
{
    private readonly List<IDomainEvent> _domainEvents = new();
    protected AggregateRoot(TId id) : base(id) { }
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents;
    protected void RaiseDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
}
```

Mỗi public type nằm trong file đúng tên đã liệt kê.

- [ ] **Step 4: Verify GREEN**

Run: `dotnet test backend/FoodDelivery.Domain.Tests --filter Raise_and_clear_domain_events`

Expected: 1 test PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/FoodDelivery.Domain/Common backend/FoodDelivery.Domain.Tests/Common
git commit -m "feat: add minimal domain primitives"
```

### Task 3: Tenant Aggregate và invariant

**Files:**
- Create: `backend/FoodDelivery.Domain/Tenancy/Tenant.cs`
- Create: `backend/FoodDelivery.Domain/Tenancy/TenantStatus.cs`
- Create: `backend/FoodDelivery.Domain.Tests/Tenancy/TenantTests.cs`

**Interfaces:**
- Produces: `Tenant.Create(Guid id, string name, string subdomain)`, `Activate()`, `Deactivate()`.

- [ ] **Step 1: Viết failing tests cho Tenant**

```csharp
using FoodDelivery.Domain.Tenancy;

namespace FoodDelivery.Domain.Tests.Tenancy;

public class TenantTests
{
    [Fact]
    public void Create_normalizes_subdomain()
    {
        var tenant = Tenant.Create(Guid.NewGuid(), "Bánh Mỳ Cay", " BanhMyCay ");
        Assert.Equal("banhmycay", tenant.Subdomain);
        Assert.Equal(TenantStatus.Active, tenant.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("-starts-with-hyphen")]
    public void Create_rejects_invalid_subdomain(string subdomain)
    {
        Assert.Throws<ArgumentException>(() => Tenant.Create(Guid.NewGuid(), "Tenant", subdomain));
    }
}
```

- [ ] **Step 2: Run tests và xác nhận RED**

Run: `dotnet test backend/FoodDelivery.Domain.Tests --filter TenantTests`

Expected: FAIL vì `Tenant` chưa tồn tại.

- [ ] **Step 3: Implement Tenant**

```csharp
using System.Text.RegularExpressions;
using FoodDelivery.Domain.Common;

namespace FoodDelivery.Domain.Tenancy;

public sealed class Tenant : AggregateRoot<Guid>
{
    private static readonly Regex ValidSubdomain = new("^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$", RegexOptions.Compiled);
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
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Tenant name is required.", nameof(name));
        if (!ValidSubdomain.IsMatch(normalized)) throw new ArgumentException("Invalid subdomain.", nameof(subdomain));
        return new Tenant(id, name.Trim(), normalized);
    }

    public void Activate() => Status = TenantStatus.Active;
    public void Deactivate() => Status = TenantStatus.Inactive;
}

public enum TenantStatus { Inactive = 0, Active = 1 }
```

Đặt enum trong file `TenantStatus.cs`.

- [ ] **Step 4: Verify GREEN**

Run: `dotnet test backend/FoodDelivery.Domain.Tests --filter TenantTests`

Expected: tất cả `TenantTests` PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/FoodDelivery.Domain/Tenancy backend/FoodDelivery.Domain.Tests/Tenancy
git commit -m "feat: add tenant aggregate"
```

### Task 4: Application contracts cho current user, tenant và persistence

**Files:**
- Create: `backend/FoodDelivery.Application/Abstractions/ICurrentUser.cs`
- Create: `backend/FoodDelivery.Application/Abstractions/ITenantContext.cs`
- Create: `backend/FoodDelivery.Application/Abstractions/IApplicationDbContext.cs`
- Create: `backend/FoodDelivery.Application/Abstractions/IDomainEventDispatcher.cs`
- Create: `backend/FoodDelivery.Application/Abstractions/IDomainEventHandler.cs`
- Modify: `backend/FoodDelivery.Application/FoodDelivery.Application.csproj`

**Interfaces:**
- Produces: application-facing contracts; consumes `Tenant`, `AggregateRoot<TId>` và `IDomainEvent` từ Domain.

- [ ] **Step 1: Thêm EF Core abstraction package**

Thêm package chỉ để dùng `DbSet<T>` trong application contract:

```xml
<PackageReference Include="Microsoft.EntityFrameworkCore" Version="6.0.36" />
```

- [ ] **Step 2: Tạo identity và tenant contracts**

```csharp
public interface ICurrentUser
{
    Guid? UserId { get; }
    Guid? TenantId { get; }
    bool IsInRole(string role);
}

public interface ITenantContext
{
    Guid TenantId { get; }
    bool HasTenant { get; }
    void Set(Guid tenantId);
}
```

- [ ] **Step 3: Tạo persistence contract nhỏ nhất**

```csharp
using FoodDelivery.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

public interface IApplicationDbContext
{
    DbSet<Tenant> Tenants { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Tạo event contracts**

```csharp
using FoodDelivery.Domain.Common;

public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task Handle(TEvent domainEvent, CancellationToken cancellationToken);
}

public interface IDomainEventDispatcher
{
    Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 5: Verify contracts compile**

Run: `dotnet build backend/FoodDelivery.Application/FoodDelivery.Application.csproj`

Expected: build PASS.

- [ ] **Step 6: Commit**

```bash
git add backend/FoodDelivery.Application
git commit -m "feat: define application boundary contracts"
```

### Task 5: EF Core persistence và TenantContext

**Files:**
- Create: `backend/FoodDelivery.Infrastructure/Persistence/ApplicationDbContext.cs`
- Create: `backend/FoodDelivery.Infrastructure/Persistence/Configurations/TenantConfiguration.cs`
- Create: `backend/FoodDelivery.Infrastructure/Tenancy/TenantContext.cs`
- Create: `backend/FoodDelivery.Infrastructure/DependencyInjection.cs`

**Interfaces:**
- Implements: `IApplicationDbContext`, `ITenantContext`.
- Produces: `AddInfrastructure(IConfiguration)` registration method.

- [ ] **Step 1: Implement scoped TenantContext**

```csharp
public sealed class TenantContext : ITenantContext
{
    private Guid? _tenantId;
    public Guid TenantId => _tenantId ?? throw new InvalidOperationException("Tenant has not been resolved.");
    public bool HasTenant => _tenantId.HasValue;
    public void Set(Guid tenantId)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (_tenantId.HasValue && _tenantId != tenantId) throw new InvalidOperationException("Tenant cannot change during a request.");
        _tenantId = tenantId;
    }
}
```

- [ ] **Step 2: Implement DbContext**

```csharp
public sealed class ApplicationDbContext : DbContext, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }
    public DbSet<Tenant> Tenants => Set<Tenant>();
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
}
```

Tenant chưa dùng global filter vì Tenant là root record. Các entity thuộc Tenant trong plan sau bắt buộc inject `ITenantContext` và áp dụng filter theo `TenantId`.

- [ ] **Step 3: Configure Tenant persistence**

```csharp
public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Subdomain).HasMaxLength(63).IsRequired();
        builder.HasIndex(x => x.Subdomain).IsUnique();
        builder.Ignore(x => x.DomainEvents);
    }
}
```

- [ ] **Step 4: Register Infrastructure**

```csharp
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Default.");

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));
        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(provider => provider.GetRequiredService<TenantContext>());
        return services;
    }
}
```

- [ ] **Step 5: Verify Infrastructure build**

Run: `dotnet build backend/FoodDelivery.Infrastructure/FoodDelivery.Infrastructure.csproj`

Expected: build PASS.

- [ ] **Step 6: Commit**

```bash
git add backend/FoodDelivery.Infrastructure
git commit -m "feat: add tenant persistence foundation"
```

### Task 6: Resolve Tenant từ authenticated user hoặc subdomain

**Files:**
- Create: `backend/FoodDelivery.Api/Auth/CurrentUser.cs`
- Create: `backend/FoodDelivery.Api/Middleware/TenantResolutionMiddleware.cs`
- Create: `backend/FoodDelivery.Api/Options/TenantResolutionOptions.cs`
- Modify: `backend/FoodDelivery.Api/Program.cs`
- Modify: `backend/FoodDelivery.Api/appsettings.json`

**Interfaces:**
- Implements: `ICurrentUser` from Task 4.
- Consumes: `TenantContext`, `ApplicationDbContext` from Task 5.
- Produces: resolved `ITenantContext` for downstream handlers.

- [ ] **Step 1: Add host configuration**

```json
"TenantResolution": {
  "BaseDomain": "food.com.vn",
  "SharedHosts": ["live.food.com.vn", "localhost"]
}
```

Bind vào:

```csharp
public sealed class TenantResolutionOptions
{
    public const string SectionName = "TenantResolution";
    public string BaseDomain { get; init; } = string.Empty;
    public string[] SharedHosts { get; init; } = Array.Empty<string>();
}
```

- [ ] **Step 2: Implement CurrentUser**

```csharp
public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;
    public CurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    public Guid? UserId => ReadGuid(ClaimTypes.NameIdentifier);
    public Guid? TenantId => ReadGuid("tenant_id");
    public bool IsInRole(string role) => _accessor.HttpContext?.User.IsInRole(role) == true;

    private Guid? ReadGuid(string claimType) =>
        Guid.TryParse(_accessor.HttpContext?.User.FindFirstValue(claimType), out var value) ? value : null;
}
```

- [ ] **Step 3: Implement middleware flow**

```text
Nếu authenticated user có tenant_id:
  query Tenant theo id + Active
  có Tenant: Set TenantContext từ claim
  không có Tenant: trả 404
Ngược lại nếu host không thuộc SharedHosts và kết thúc bằng BaseDomain:
  lấy đúng một label ngay trước BaseDomain làm subdomain
  query Tenant theo normalized subdomain + Active
  có Tenant: Set TenantContext
  không có Tenant: trả 404
Ngược lại:
  tiếp tục không Tenant; endpoint cần Tenant tự từ chối
```

Không nhận TenantId từ header/query/body.

```csharp
public sealed class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    public TenantResolutionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext httpContext,
        ICurrentUser currentUser,
        TenantContext tenantContext,
        ApplicationDbContext dbContext,
        IOptions<TenantResolutionOptions> options)
    {
        Tenant? tenant = null;
        if (currentUser.TenantId is Guid claimTenantId)
        {
            tenant = await dbContext.Tenants.SingleOrDefaultAsync(
                x => x.Id == claimTenantId && x.Status == TenantStatus.Active,
                httpContext.RequestAborted);
        }
        else
        {
            var host = httpContext.Request.Host.Host.ToLowerInvariant();
            var settings = options.Value;
            var isShared = settings.SharedHosts.Contains(host, StringComparer.OrdinalIgnoreCase);
            var suffix = "." + settings.BaseDomain.ToLowerInvariant();
            if (!isShared && host.EndsWith(suffix, StringComparison.Ordinal))
            {
                var subdomain = host[..^suffix.Length];
                if (!subdomain.Contains('.'))
                    tenant = await dbContext.Tenants.SingleOrDefaultAsync(
                        x => x.Subdomain == subdomain && x.Status == TenantStatus.Active,
                        httpContext.RequestAborted);
            }
        }

        if (currentUser.TenantId.HasValue || !options.Value.SharedHosts.Contains(httpContext.Request.Host.Host, StringComparer.OrdinalIgnoreCase))
        {
            if (tenant is null)
            {
                httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            tenantContext.Set(tenant.Id);
        }

        await _next(httpContext);
    }
}
```

- [ ] **Step 4: Wire Api composition root**

Trong `Program.cs`:

```csharp
builder.Services.AddHttpContextAccessor();
builder.Services.Configure<TenantResolutionOptions>(builder.Configuration.GetSection(TenantResolutionOptions.SectionName));
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddInfrastructure(builder.Configuration);
// ...
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();
```

Giữ middleware exception trước tenant middleware. Không đăng ký song song `AppDbContext` cũ và `ApplicationDbContext` cho module mới; Category cũ chỉ được giữ tạm tới plan Catalog migration.

- [ ] **Step 5: Verify API build**

Run: `dotnet build backend/FoodDelivery.Api/FoodDelivery.Api.csproj`

Expected: build PASS.

- [ ] **Step 6: Commit**

```bash
git add backend/FoodDelivery.Api/Auth backend/FoodDelivery.Api/Middleware backend/FoodDelivery.Api/Options backend/FoodDelivery.Api/Program.cs backend/FoodDelivery.Api/appsettings.json
git commit -m "feat: resolve tenant from user or subdomain"
```

### Task 7: Domain Event dispatcher không thêm dependency

**Files:**
- Create: `backend/FoodDelivery.Infrastructure/Events/DomainEventDispatcher.cs`
- Modify: `backend/FoodDelivery.Infrastructure/DependencyInjection.cs`
- Create: `backend/FoodDelivery.Infrastructure.Tests/FoodDelivery.Infrastructure.Tests.csproj`
- Create: `backend/FoodDelivery.Infrastructure.Tests/Events/DomainEventDispatcherTests.cs`
- Modify: `backend/FoodDelivery.slnx`

**Interfaces:**
- Implements: `IDomainEventDispatcher.DispatchAsync(IEnumerable<IDomainEvent>, CancellationToken)`.
- Consumes: registered `IDomainEventHandler<TEvent>` implementations.

- [ ] **Step 1: Tạo Infrastructure test project**

Dùng cùng ba test packages ở Task 1, reference `FoodDelivery.Application`, `FoodDelivery.Domain`, `FoodDelivery.Infrastructure`, và thêm `Microsoft.Extensions.DependencyInjection` version `6.0.1`.

- [ ] **Step 2: Viết failing dispatcher test**

```csharp
private sealed record TestEvent : IDomainEvent;
private sealed class TestHandler : IDomainEventHandler<TestEvent>
{
    public int Calls { get; private set; }
    public Task Handle(TestEvent domainEvent, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.CompletedTask;
    }
}

[Fact]
public async Task Dispatches_event_to_registered_handler_once()
{
    var services = new ServiceCollection();
    var handler = new TestHandler();
    services.AddSingleton<IDomainEventHandler<TestEvent>>(handler);
    await using var provider = services.BuildServiceProvider();

    var dispatcher = new DomainEventDispatcher(provider);
    await dispatcher.DispatchAsync(new IDomainEvent[] { new TestEvent() });

    Assert.Equal(1, handler.Calls);
}
```

- [ ] **Step 3: Run test và xác nhận RED**

Run: `dotnet test backend/FoodDelivery.Infrastructure.Tests --filter Dispatches_event_to_registered_handler_once`

Expected: FAIL vì dispatcher chưa tồn tại.

- [ ] **Step 4: Implement dispatcher tối thiểu**

```csharp
public sealed class DomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    public DomainEventDispatcher(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;

    public async Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken = default)
    {
        foreach (var domainEvent in events)
        {
            var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());
            foreach (var handler in _serviceProvider.GetServices(handlerType))
            {
                var method = handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.Handle))!;
                await (Task)method.Invoke(handler, new object[] { domainEvent, cancellationToken })!;
            }
        }
    }
}
```

Giữ reflection duy nhất trong class này; không thêm MediatR.

- [ ] **Step 5: Verify GREEN và full backend suite**

Run: `dotnet test backend/FoodDelivery.Infrastructure.Tests --filter Dispatches_event_to_registered_handler_once`

Expected: 1 test PASS.

Run: `dotnet test backend/FoodDelivery.slnx`

Expected: toàn bộ test PASS.

- [ ] **Step 6: Register dispatcher và commit**

Đăng ký `IDomainEventDispatcher` scoped trong `AddInfrastructure`.

```bash
git add backend/FoodDelivery.Infrastructure backend/FoodDelivery.Infrastructure.Tests backend/FoodDelivery.slnx
git commit -m "feat: dispatch domain events in process"
```

### Task 8: Migration và smoke verification

**Files:**
- Create: `backend/FoodDelivery.Infrastructure/Persistence/Migrations/<generated>-AddTenants.cs`
- Create: `backend/FoodDelivery.Infrastructure/Persistence/Migrations/ApplicationDbContextModelSnapshot.cs`
- Modify: `README.md`

**Interfaces:**
- Produces: `Tenants` table với unique `Subdomain`; documented run commands.

- [ ] **Step 1: Generate migration**

Run:

```bash
dotnet ef migrations add AddTenants --project backend/FoodDelivery.Infrastructure --startup-project backend/FoodDelivery.Api --output-dir Persistence/Migrations
```

Expected: migration tạo bảng `Tenants`, primary key `Id`, unique index `Subdomain`.

- [ ] **Step 2: Update README commands**

Thay lệnh migration cũ bằng:

```bash
dotnet ef database update --project FoodDelivery.Infrastructure --startup-project FoodDelivery.Api
```

Thêm đúng nội dung:

```markdown
### Tenant resolution

`TenantResolution:BaseDomain` là domain gốc theo môi trường. Request tới một host trong
`SharedHosts` lấy Tenant từ claim `tenant_id`; request tới `<subdomain>.<BaseDomain>`
lấy Tenant theo subdomain. Client không được truyền `TenantId` để tự chọn Tenant.
```

- [ ] **Step 3: Run full verification**

Run: `dotnet build backend/FoodDelivery.slnx`

Expected: build PASS, 0 errors.

Run: `dotnet test backend/FoodDelivery.slnx`

Expected: toàn bộ tests PASS.

Run với MySQL đang hoạt động: `dotnet ef database update --project backend/FoodDelivery.Infrastructure --startup-project backend/FoodDelivery.Api`

Expected: migration apply thành công; bảng `Tenants` và unique index được tạo.

- [ ] **Step 4: Commit**

```bash
git add backend/FoodDelivery.Infrastructure/Persistence/Migrations README.md
git commit -m "docs: add tenant migration workflow"
```

## Completion Gate

- `Domain` không tham chiếu project hoặc framework khác.
- `Application`, `Infrastructure`, `Api` build theo đúng dependency direction.
- Tenant subdomain được normalize và validate trong Domain.
- Request không lấy TenantId từ input không đáng tin cậy.
- Tenant được resolve từ authenticated claim hoặc subdomain cấu hình.
- Domain Event dispatcher chạy in-process mà không thêm mediator/broker.
- Full backend test suite và migration command đều thành công.
