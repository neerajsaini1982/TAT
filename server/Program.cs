var builder = WebApplication.CreateBuilder(args);

const string AngularDevClient = "AngularDevClient";

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
{
    options.AddPolicy(AngularDevClient, policy =>
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors(AngularDevClient);
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
