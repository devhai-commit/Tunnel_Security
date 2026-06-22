var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuth(builder.Configuration); // your extension already configures JWT & Db
builder.Services.AddControllers();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.Run();