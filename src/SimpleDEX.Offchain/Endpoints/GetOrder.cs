using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using SimpleDEX.Data;
using SimpleDEX.Offchain.Models;

namespace SimpleDEX.Offchain.Endpoints;

public class GetOrderRequest
{
    public string OutRef { get; set; } = string.Empty;
}

public class GetOrder(SimpleDEXDbContext db) : Endpoint<GetOrderRequest, OrderDto>
{
    public override void Configure()
    {
        Get("/api/v1/orders/{OutRef}");
        AllowAnonymous();
    }

    public override async Task HandleAsync(GetOrderRequest req, CancellationToken ct)
    {
        var order = await db.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.OutRef == req.OutRef, ct);

        if (order is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.ResponseAsync(new OrderDto(
            order.OutRef,
            order.OwnerPkh,
            order.DestinationAddress,
            order.OfferSubject,
            order.AskSubject,
            order.PriceNum,
            order.PriceDen,
            order.ScriptHash,
            order.Slot,
            order.Status.ToString(),
            order.SpentSlot
        ), cancellation: ct);
    }
}
