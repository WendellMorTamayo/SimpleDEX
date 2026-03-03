using Chrysalis.Wallet.Models.Addresses;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using SimpleDEX.Data;
using SimpleDEX.Offchain.Models;
using OrderEntity = SimpleDEX.Data.Models.Order;
using OrderStatus = SimpleDEX.Data.Models.OrderStatus;

namespace SimpleDEX.Offchain.Endpoints;

public class ListOrders(SimpleDEXDbContext db) : Endpoint<ListOrdersRequest, ListOrdersResponse>
{
    public override void Configure()
    {
        Post("/api/v1/orders");
        AllowAnonymous();
    }

    public override async Task HandleAsync(ListOrdersRequest req, CancellationToken ct)
    {
        if (req.Page < 1) req.Page = 1;
        if (req.PageSize < 1) req.PageSize = 20;
        if (req.PageSize > 100) req.PageSize = 100;

        IQueryable<OrderEntity> query = db.Orders.AsNoTracking();

        if (!string.IsNullOrEmpty(req.Status) && Enum.TryParse<OrderStatus>(req.Status, ignoreCase: true, out var status))
            query = query.Where(o => o.Status == status);

        if (!string.IsNullOrEmpty(req.OfferSubject))
            query = query.Where(o => o.OfferSubject == req.OfferSubject);

        if (!string.IsNullOrEmpty(req.AskSubject))
            query = query.Where(o => o.AskSubject == req.AskSubject);

        if (!string.IsNullOrEmpty(req.OwnerAddress))
        {
            string ownerPkh = Convert.ToHexStringLower(new Address(req.OwnerAddress).GetPaymentKeyHash()!);
            query = query.Where(o => o.OwnerPkh == ownerPkh);
        }

        if (!string.IsNullOrEmpty(req.ScriptHash))
            query = query.Where(o => o.ScriptHash == req.ScriptHash);

        int totalCount = await query.CountAsync(ct);

        IEnumerable<OrderDto> items = await query
            .OrderByDescending(o => o.Slot)
            .Skip((req.Page - 1) * req.PageSize)
            .Take(req.PageSize)
            .Select(o => new OrderDto(
                o.OutRef,
                o.OwnerPkh,
                o.DestinationAddress,
                o.OfferSubject,
                o.AskSubject,
                o.PriceNum,
                o.PriceDen,
                o.ScriptHash,
                o.Slot,
                o.Status.ToString(),
                o.SpentSlot
            ))
            .ToListAsync(ct);

        await Send.ResponseAsync(new ListOrdersResponse(items, req.Page, req.PageSize, totalCount), cancellation: ct);
    }
}
