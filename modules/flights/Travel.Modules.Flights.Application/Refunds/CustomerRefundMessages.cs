namespace Travel.Modules.Flights.Application.Refunds;

public sealed record AssessCustomerRefund(Guid AggregateId, Guid CancellationId)
{
    public override string ToString() => nameof(AssessCustomerRefund);
}

public sealed record ExecuteCustomerRefund(Guid AggregateId, Guid RefundId)
{
    public override string ToString() => nameof(ExecuteCustomerRefund);
}

public sealed record CustomerRefundDeadline(
    Guid AggregateId,
    Guid RefundId,
    DateTimeOffset DeadlineAt
)
{
    public override string ToString() => nameof(CustomerRefundDeadline);
}

public sealed record CustomerRefundAvailability(bool CanExecute);
