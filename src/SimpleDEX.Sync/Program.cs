using Argus.Sync.Data.Models;
using Argus.Sync.Extensions;
using SimpleDEX.Data;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddCardanoIndexer<SimpleDEXDbContext>(builder.Configuration);
builder.Services.AddReducers<SimpleDEXDbContext, IReducerModel>(builder.Configuration);

WebApplication app = builder.Build();

app.Run();
