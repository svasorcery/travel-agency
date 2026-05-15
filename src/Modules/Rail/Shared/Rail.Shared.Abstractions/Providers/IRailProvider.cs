namespace Rail.Shared.Abstractions.Providers
{
    public interface IRailProvider
    {
        Task<IEnumerable<StationDto>> SearchStationsAsync(SearchStations query);
        Task<IEnumerable<TrainDto>> SearchTrainsAsync(SearchTrains query);
        Task<IEnumerable<TrainStationDto>> GetTrainRouteAsync(GetTrainStations query);
        Task<IEnumerable<CarDto>> GetCarsAsync(GetCars query);
    }
}
