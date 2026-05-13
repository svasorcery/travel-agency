# Flights module

**Status:** каркас (production-grade реализация в подпроекте 1)

## Bounded context
Поиск и бронирование авиабилетов. Mixed bookable + deeplink aggregation (Duffel + Travelpayouts).

## Aggregates
_TBD в подпроекте 1: BookingAggregate с состояниями OfferQuoted → Held → Confirmed → Ticketed → Refunded → Cancelled_

## Domain Events
_TBD в подпроекте 1_

## External integrations
_TBD в подпроекте 1: Duffel (bookable), Travelpayouts (deeplink)_

## Tests
- Unit:        `tests/flights/Travel.Modules.Flights.Tests.Unit/`
- Integration: `tests/flights/Travel.Modules.Flights.Tests.Integration/`
