using System.Text.Encodings.Web;
using FoodDelivery.Application.Abstractions;
using FoodDelivery.Application.Abstractions.Messaging;
using FoodDelivery.Infrastructure.Messaging;
using FoodDelivery.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;

namespace FoodDelivery.Api.Diagnostics;

internal static class MessagingDiagnosticsEndpoints
{
    private const string QueueName = "food.diagnostics";
    private const string RoutingKey = "diagnostics.ping.v1";

    internal static void MapMessagingDiagnostics(this WebApplication app)
    {
        app.MapGet("/dev/messaging", ShowAsync);
        app.MapPost("/dev/messaging/publish", PublishAsync);
    }

    private static async Task<IResult> ShowAsync(
        RabbitMqConnectionManager manager,
        RabbitMqOptions options,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var messageCount = await EnsureTopologyAsync(manager, options, cancellationToken);
        var rows = await dbContext.OutboxMessages.AsNoTracking()
            .OrderByDescending(message => message.OccurredAtUtc)
            .Take(20)
            .Select(message => new
            {
                message.Id,
                message.EventName,
                message.OccurredAtUtc,
                message.SentAtUtc,
                message.Attempts,
                message.LastError
            })
            .ToListAsync(cancellationToken);
        var tableRows = string.Join(Environment.NewLine, rows.Select(row =>
            $@"<tr><td><code>{row.Id}</code></td><td>{Encode(row.EventName)}</td>
            <td>{row.OccurredAtUtc:O}</td><td>{(row.SentAtUtc is null ? "Pending" : "Sent")}</td>
            <td>{row.Attempts}</td><td>{Encode(row.LastError ?? "")}</td></tr>"));
        var html = $@"<!doctype html><html lang=""vi""><head><meta charset=""utf-8"">
            <title>Messaging Diagnostics</title>
            <style>body{{font:14px system-ui;max-width:1200px;margin:32px auto;padding:0 16px}}button{{padding:10px 16px}}
            table{{border-collapse:collapse;width:100%;margin-top:24px}}th,td{{border:1px solid #ccc;padding:8px;text-align:left}}</style>
            </head><body><h1>Messaging Diagnostics</h1>
            <p>Queue <code>{QueueName}</code> hiện có <strong>{messageCount}</strong> message chờ đọc.</p>
            <form method=""post"" action=""/dev/messaging/publish""><button type=""submit"">Gửi test event qua Outbox</button></form>
            <p>Sau khi bấm, tải lại trang để xem trạng thái publish.</p>
            <table><thead><tr><th>MessageId</th><th>Event</th><th>OccurredAtUtc</th><th>State</th><th>Attempts</th><th>LastError</th></tr></thead>
            <tbody>{tableRows}</tbody></table></body></html>";
        return Results.Content(html, "text/html; charset=utf-8");
    }

    private static async Task<IResult> PublishAsync(
        IUnitOfWorkFactory unitOfWorkFactory,
        RabbitMqConnectionManager manager,
        RabbitMqOptions options,
        CancellationToken cancellationToken)
    {
        await EnsureTopologyAsync(manager, options, cancellationToken);
        var integrationEvent = DiagnosticPingEvent.Create();
        await using var unitOfWork = await unitOfWorkFactory.CreateAsync(cancellationToken);
        unitOfWork.EnqueueIntegrationEvent(integrationEvent, RoutingKey);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.Redirect("/dev/messaging");
    }

    private static async Task<uint> EnsureTopologyAsync(
        RabbitMqConnectionManager manager,
        RabbitMqOptions options,
        CancellationToken cancellationToken)
    {
        var connection = await manager.GetConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(options.ExchangeName, ExchangeType.Direct, true, false,
            cancellationToken: cancellationToken);
        var queue = await channel.QueueDeclareAsync(QueueName, false, false, true,
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(QueueName, options.ExchangeName, RoutingKey,
            cancellationToken: cancellationToken);
        return queue.MessageCount;
    }

    private static string Encode(string value) => HtmlEncoder.Default.Encode(value);

    private sealed record DiagnosticPingEvent(
        Guid MessageId,
        string EventName,
        int ContractVersion,
        DateTimeOffset OccurredAtUtc,
        string? CorrelationId,
        Guid? TenantId,
        Guid? ShopId,
        string Message) : IIntegrationEvent
    {
        internal static DiagnosticPingEvent Create() => new(
            Guid.NewGuid(), "DiagnosticPing", 1, DateTimeOffset.UtcNow,
            $"diagnostic-{Guid.NewGuid():N}", Guid.NewGuid(), Guid.NewGuid(), "RabbitMQ is reachable");
    }
}
