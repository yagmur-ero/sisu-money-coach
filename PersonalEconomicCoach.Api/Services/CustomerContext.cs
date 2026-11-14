namespace PersonalEconomicCoach.Api.Services;

public interface ICustomerContext
{
    string CustomerId { get; }
}

/// <summary>Development-only identity. Replace with a verified auth claim before production.</summary>
public sealed class DemoCustomerContext(IConfiguration configuration) : ICustomerContext
{
    public string CustomerId => configuration["App:DemoCustomerId"] ?? "CUST00001";
}
