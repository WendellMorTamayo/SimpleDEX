using Chrysalis.Tx.Extensions;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using SimpleDEX.Data;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddFastEndpoints();
builder.Services.AddOpenApi();
builder.Services.AddHttpClient();
builder.Services.AddCardanoProvider(builder.Configuration);
builder.Services.AddDbContext<SimpleDEXDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("SimpleDEX")));

WebApplication app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseFastEndpoints();
app.Run();
