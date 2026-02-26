namespace SimpleDEX.Offchain.Models;

public class ListOrdersRequest
{
    public string? Status { get; set; }
    public string? OfferSubject { get; set; }
    public string? AskSubject { get; set; }
    public string? OwnerAddress { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public record ListOrdersResponse(
    List<OrderDto> Items,
    int Page,
    int PageSize,
    int TotalCount
);
