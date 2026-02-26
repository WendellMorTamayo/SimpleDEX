using Chrysalis.Tx.Extensions;
using FastEndpoints;
using Scalar.AspNetCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddFastEndpoints();
builder.Services.AddOpenApi();
builder.Services.AddHttpClient();
builder.Services.AddCardanoProvider(builder.Configuration);

WebApplication app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseFastEndpoints();
app.Run();
