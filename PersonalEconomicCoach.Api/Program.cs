using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using PersonalEconomicCoach.Api.Data;
using PersonalEconomicCoach.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var message = string.Join(" ", context.ModelState.Values
            .SelectMany(value => value.Errors)
            .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                ? "Invalid request value."
                : error.ErrorMessage)
            .Distinct());

        return new BadRequestObjectResult(new { error = message });
    };
});

var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy("Frontend", policy =>
{
    if (origins.Contains("*", StringComparer.Ordinal))
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    else
        policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
}));

builder.Services.AddSingleton<IDbConnectionFactory, MySqlConnectionFactory>();
builder.Services.AddScoped<ICustomerContext, DemoCustomerContext>();
builder.Services.AddSingleton<IDateProvider, HelsinkiDateProvider>();
builder.Services.AddScoped<IQuestionRouter, QuestionRouter>();
builder.Services.AddScoped<IInsightService, InsightService>();
builder.Services.AddScoped<IEventService, EventService>();
builder.Services.AddScoped<IPlanService, PlanService>();
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddScoped<ITransactionService, TransactionService>();
builder.Services.AddScoped<ISetupService, SetupService>();
builder.Services.AddScoped<IConversationService, ConversationService>();
builder.Services.AddHttpClient<IAnswerGenerator, OpenAiAnswerGenerator>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
});

var app = builder.Build();
app.UseCors("Frontend");
app.UseMiddleware<ApiExceptionMiddleware>();
app.MapControllers();
app.MapFallback(async context =>
{
    context.Response.StatusCode = StatusCodes.Status404NotFound;
    await context.Response.WriteAsJsonAsync(new { error = "Endpoint not found." });
});
app.Run();

public partial class Program { }
