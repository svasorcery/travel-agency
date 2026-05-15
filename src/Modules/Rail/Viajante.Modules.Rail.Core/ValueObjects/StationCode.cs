namespace Rail.Shared.Abstractions.ValueObjects
{
    public record StationCode
    {
        public string Value { get; }

        public StationCode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidStationCodeException(value);
            }

            Value = value.Trim().ToUpperInvariant();
        }
    }
}
