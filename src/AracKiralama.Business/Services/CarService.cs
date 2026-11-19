using AracKiralama.Business.Auditing;
using AracKiralama.Business.Validation;
using AracKiralama.Domain.Entities;
using AracKiralama.Domain.Repositories;
using FluentValidation;

namespace AracKiralama.Business.Services;

/// <summary>A car as the admin's form enters it; texts as typed, checked by the service.</summary>
public sealed record CarInput(
    string? Brand,
    string? Model,
    string? Year,
    string? Transmission,
    string? FuelType,
    string? Mileage,
    string? Color,
    string? Plate,
    string? DailyPrice,
    string? ImageUrl,
    bool IsListed);

public interface ICarService
{
    /// <summary>Listed cars that match <paramref name="filter"/>.</summary>
    Task<IReadOnlyList<Car>> GetCatalogAsync(UserSession session, CarFilter filter, CancellationToken cancellationToken = default);

    /// <summary>Brands, gearboxes and fuel types to filter by.</summary>
    Task<CarFilterOptions> GetFilterOptionsAsync(UserSession session, CancellationToken cancellationToken = default);

    /// <summary>A car a customer may rent; an unlisted car is treated as missing for non-admins.</summary>
    /// <exception cref="BusinessRuleException">The car does not exist (or is unlisted).</exception>
    Task<Car> GetAsync(UserSession session, int carId, CancellationToken cancellationToken = default);

    /// <summary>Every car (admin only).</summary>
    Task<IReadOnlyList<Car>> GetAllAsync(UserSession session, CancellationToken cancellationToken = default);

    /// <summary>Adds a car (admin only) and returns its id.</summary>
    Task<int> AddAsync(UserSession session, CarInput input, CancellationToken cancellationToken = default);

    /// <summary>Changes a car (admin only).</summary>
    Task UpdateAsync(UserSession session, int carId, CarInput input, CancellationToken cancellationToken = default);

    /// <summary>Deletes a car that has never been rented (admin only).</summary>
    Task DeleteAsync(UserSession session, int carId, CancellationToken cancellationToken = default);
}

internal sealed class CarService(
    ICarRepository cars,
    AccessGuard access,
    TimeProvider timeProvider,
    IAuditLog audit,
    IValidator<CarFields> validator) : ICarService
{
    internal const string NotFound = "Araç bulunamadı.";
    internal const string InputInvalid = "Lütfen gerekli alanları doldurun.";
    internal const string PlateTaken = "Bu plaka ile kayıtlı bir araç zaten mevcut.";
    internal const string HasRentals = "Kiralama veya bakım geçmişi olan araç silinemez. Kiralamaya kapatmak için \"Kiralamaya açık\" seçeneğini kaldırın.";

    public async Task<IReadOnlyList<Car>> GetCatalogAsync(UserSession session, CarFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        await access.RequireActiveAsync(session, cancellationToken).ConfigureAwait(false);
        return await cars.GetCatalogAsync(filter, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CarFilterOptions> GetFilterOptionsAsync(UserSession session, CancellationToken cancellationToken = default)
    {
        await access.RequireActiveAsync(session, cancellationToken).ConfigureAwait(false);
        return await cars.GetFilterOptionsAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Car> GetAsync(UserSession session, int carId, CancellationToken cancellationToken = default)
    {
        var role = await access.RequireActiveAsync(session, cancellationToken).ConfigureAwait(false);
        var car = await cars.FindAsync(carId, cancellationToken).ConfigureAwait(false);
        if (car is null || (!car.IsListed && role != UserRole.Admin))
        {
            throw new BusinessRuleException(NotFound);
        }

        return car;
    }

    public async Task<IReadOnlyList<Car>> GetAllAsync(UserSession session, CancellationToken cancellationToken = default)
    {
        await access.RequireAdminAsync(session, cancellationToken).ConfigureAwait(false);
        return await cars.GetAllAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> AddAsync(UserSession session, CarInput input, CancellationToken cancellationToken = default)
    {
        await access.RequireAdminAsync(session, cancellationToken).ConfigureAwait(false);
        var car = Validate(input, stored: null);
        try
        {
            var id = await access.WriteAsActorAsync(session, () => cars.AddAsync(session.Actor, car, cancellationToken)).ConfigureAwait(false);
            audit.Record(AuditEvent.CarChanged, AuditRole.Admin);
            return id;
        }
        catch (DuplicatePlateException ex)
        {
            throw PlateTakenError(ex);
        }
    }

    public async Task UpdateAsync(UserSession session, int carId, CarInput input, CancellationToken cancellationToken = default)
    {
        await access.RequireAdminAsync(session, cancellationToken).ConfigureAwait(false);
        var stored = await cars.FindAsync(carId, cancellationToken).ConfigureAwait(false)
            ?? throw new BusinessRuleException(NotFound);
        var car = Validate(input, stored);
        car.Id = carId;

        CarUpdateOutcome outcome;
        try
        {
            outcome = await access.WriteAsActorAsync(session, () => cars.UpdateAsync(session.Actor, car, cancellationToken)).ConfigureAwait(false);
        }
        catch (DuplicatePlateException ex)
        {
            throw PlateTakenError(ex);
        }

        if (outcome != CarUpdateOutcome.Updated)
        {
            throw new BusinessRuleException(NotFound);
        }

        audit.Record(AuditEvent.CarChanged, AuditRole.Admin);
    }

    public async Task DeleteAsync(UserSession session, int carId, CancellationToken cancellationToken = default)
    {
        await access.RequireAdminAsync(session, cancellationToken).ConfigureAwait(false);
        var outcome = await access.WriteAsActorAsync(session, () => cars.DeleteAsync(session.Actor, carId, cancellationToken)).ConfigureAwait(false);
        switch (outcome)
        {
            case CarDeleteOutcome.Deleted:
                audit.Record(AuditEvent.CarChanged, AuditRole.Admin);
                return;
            case CarDeleteOutcome.HasRentals:
                throw new BusinessRuleException(HasRentals);
            default:
                throw new BusinessRuleException(NotFound);
        }
    }

    private static BusinessRuleException PlateTakenError(DuplicatePlateException cause) =>
        new(PlateTaken, new Dictionary<string, string> { [nameof(CarInput.Plate)] = PlateTaken }, cause);

    /// <summary>
    /// Checks every field of <paramref name="input"/> with the validator, which reports one message per
    /// invalid field, as the original form marked each empty field; nothing is stored unless all are
    /// valid. A change of a <paramref name="stored"/> car keeps a gearbox or fuel type of the original
    /// data that is in neither list as long as the form sends it unchanged.
    /// </summary>
    private Car Validate(CarInput input, Car? stored)
    {
        var fields = CarFields.Create(input, timeProvider.Today().Year + 1, stored);
        var result = validator.Validate(fields);
        if (!result.IsValid)
        {
            // One message per field (each rule stops at its first failure), in the order of the form.
            var errors = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var failure in result.Errors)
            {
                errors.TryAdd(failure.PropertyName, failure.ErrorMessage);
            }

            throw new BusinessRuleException(InputInvalid + "\n\n" + string.Join("\n", errors.Values), errors);
        }

        var price = InputRules.TryParseMoney(fields.DailyPrice, out var parsed) ? parsed : 0m;
        return new Car
        {
            Brand = fields.Brand!.Trim(),
            Model = fields.Model!.Trim(),
            Year = CarFieldsValidator.ParseNumber(fields.Year)!.Value,
            Transmission = CarFieldsValidator.ChoiceValue(fields.Transmission, fields.StoredTransmission),
            FuelType = CarFieldsValidator.ChoiceValue(fields.FuelType, fields.StoredFuelType),
            Mileage = CarFieldsValidator.ParseNumber(fields.Mileage)!.Value,
            Color = fields.Color!.Trim(),
            Plate = InputRules.NormalizePlate(fields.Plate)!,
            DailyPrice = price,
            ImageUrl = string.IsNullOrWhiteSpace(fields.ImageUrl) ? null : fields.ImageUrl.Trim(),
            IsListed = input.IsListed,
        };
    }
}