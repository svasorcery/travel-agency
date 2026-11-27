using System.Diagnostics.Metrics;

namespace Travel.Modules.Trips.Application.Itineraries;

public static class TripItineraryMeasurements
{
    public const string MeterName = "Travel.Trips.Itineraries";
    public const string PreviewName = "trips.option.preview.requests";
    public const string PackageReadName = "trips.itinerary.package.reads";
    private static readonly Meter Meter = new(MeterName);
    public static Counter<long> Previews { get; } = Meter.CreateCounter<long>(PreviewName);
    public static Counter<long> PackageReads { get; } = Meter.CreateCounter<long>(PackageReadName);
}
