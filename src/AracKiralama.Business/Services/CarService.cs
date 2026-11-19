using System.Globalization;
using AracKiralama.Business.Auditing;
using AracKiralama.Business.Validation;
using AracKiralama.Domain.Entities;
using AracKiralama.Domain.Repositories;

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

internal sealed class CarService(ICarRepository cars, AccessGuard access, TimeProvider timeProvider, IAuditLog audit) : ICarService
{
    internal const string NotFound = "Araç bulunamadı.";
    internal const string InputInvalid = "Lütfen gerekli alanları doldurun.";
    internal const string PlateTaken = "Bu plaka ile kayıtlı bir araç zaten mevcut.";
    internal const string HasRentals = "Kiralama geçmişi olan araç silinemez. Kiralamaya kapatmak için \"Kiralamaya açık\" seçeneğini kaldırın.";

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
            var id = await access.WriteAsActorAsync(session, () => cars.AddAsync(session.UserId, car, cancellationToken)).ConfigureAwait(false);
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
            outcome = await access.WriteAsActorAsync(session, () => cars.UpdateAsync(session.UserId, car, cancellationToken)).ConfigureAwait(false);
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
        var outcome = await access.WriteAsActorAsync(session, () => cars.DeleteAsync(session.UserId, carId, cancellationToken)).ConfigureAwait(false);
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
    /// Checks every field of <paramref name="input"/> and collects one message per invalid field, as the
    /// original form marked each empty field; nothing is stored unless all are valid. A change of a
    /// <paramref name="stored"/> car keeps a gearbox or fuel type of the original data that is in
    /// neither list as long as the form sends it unchanged.
    /// </summary>
    private Car Validate(CarInput input, Car? stored)
    {
        ArgumentNullException.ThrowIfNull(input);

        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        var brand = Text(input.Brand, nameof(CarInput.Brand), "Marka", InputRules.BrandMaxLength, errors);
        var model = Text(input.Model, nameof(CarInput.Model), "Model", InputRules.ModelMaxLength, errors);
        var color = Text(input.Color, nameof(CarInput.Color), "Renk", InputRules.ColorMaxLength, errors);
        var transmission = Choice(input.Transmission, nameof(CarInput.Transmission), "Vites tipi", InputRules.Transmissions, stored?.Transmission, errors);
        var fuelType = Choice(input.FuelType, nameof(CarInput.FuelType), "Yakıt tipi", InputRules.FuelTypes, stored?.FuelType, errors);

        var maxYear = timeProvider.Today().Year + 1;
        var year = Number(input.Year, nameof(CarInput.Year), "Yıl", InputRules.MinYear, maxYear, errors);
        var mileage = Number(input.Mileage, nameof(CarInput.Mileage), "Kilometre", 0, InputRules.MaxMileage, errors);

        var plate = InputRules.NormalizePlate(input.Plate);
        if (string.IsNullOrWhiteSpace(input.Plate))
        {
            errors[nameof(CarInput.Plate)] = "Plaka boş bırakılamaz.";
        }
        else if (plate is null)
        {
            errors[nameof(CarInput.Plate)] = string.Create(
                CultureInfo.InvariantCulture,
                $"Plaka {InputRules.PlateMinLength}-{InputRules.PlateMaxLength} karakter olmalı; yalnızca harf (A-Z), rakam, boşluk ve - içerebilir.");
        }

        decimal price = 0m;
        if (string.IsNullOrWhiteSpace(input.DailyPrice))
        {
            errors[nameof(CarInput.DailyPrice)] = "Ücret boş bırakılamaz.";
        }
        else if (!InputRules.TryParseMoney(input.DailyPrice, out price) || price <= 0m || price > InputRules.MaxDailyPrice)
        {
            errors[nameof(CarInput.DailyPrice)] = "Ücret sıfırdan büyük, en fazla iki ondalıklı bir tutar olmalıdır (örnek: 850,50).";
        }

        var imageUrl = string.IsNullOrWhiteSpace(input.ImageUrl) ? null : input.ImageUrl.Trim();
        if (imageUrl is not null && !InputRules.IsValidImageUrl(imageUrl))
        {
            errors[nameof(CarInput.ImageUrl)] = "Resim linki http:// veya https:// ile başlayan, en fazla 255 karakterlik bir adres olmalıdır.";
        }

        if (errors.Count > 0)
        {
            throw new BusinessRuleException(InputInvalid + "\n\n" + string.Join("\n", errors.Values), errors);
        }

        return new Car
        {
            Brand = brand!,
            Model = model!,
            Year = year,
            Transmission = transmission!,
            FuelType = fuelType!,
            Mileage = mileage,
            Color = color!,
            Plate = plate!,
            DailyPrice = price,
            ImageUrl = imageUrl,
            IsListed = input.IsListed,
        };
    }

    private static string? Text(string? value, string field, string label, int maxLength, Dictionary<string, string> errors)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            errors[field] = label + " boş bırakılamaz.";
            return null;
        }

        if (text.Length > maxLength)
        {
            errors[field] = string.Create(CultureInfo.InvariantCulture, $"{label} en fazla {maxLength} karakter olabilir.");
            return null;
        }

        return text;
    }

    private static string? Choice(string? value, string field, string label, IReadOnlyList<string> allowed, string? unchanged, Dictionary<string, string> errors)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            errors[field] = label + " boş bırakılamaz.";
            return null;
        }

        if (unchanged is not null && string.Equals(text, unchanged.Trim(), StringComparison.Ordinal))
        {
            return unchanged;
        }

        var match = allowed.FirstOrDefault(a => string.Equals(a, text, StringComparison.Ordinal));
        if (match is null)
        {
            errors[field] = label + " listedeki seçeneklerden biri olmalıdır.";
        }

        return match;
    }

    private static int Number(string? value, string field, string label, int min, int max, Dictionary<string, string> errors)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            errors[field] = label + " boş bırakılamaz.";
            return 0;
        }

        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < min || number > max)
        {
            errors[field] = string.Create(CultureInfo.InvariantCulture, $"{label} {min} ile {max} arasında olmalıdır.");
            return 0;
        }

        return number;
    }
}
