namespace FoodDelivery.Domain.Common;

public sealed class DomainException : Exception
{
    public DomainException(string code, string message) : base(message) => Code = code;

    public string Code { get; }
}
