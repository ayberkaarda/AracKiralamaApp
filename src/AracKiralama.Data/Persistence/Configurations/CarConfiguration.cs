using AracKiralama.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AracKiralama.Data.Persistence.Configurations;

internal sealed class CarConfiguration : IEntityTypeConfiguration<Car>
{
    public void Configure(EntityTypeBuilder<Car> builder)
    {
        builder.ToTable("Araclar", table =>
        {
            table.HasCheckConstraint("CK_Araclar_GunlukUcret", SchemaLimits.DailyPriceCheck);
            table.HasCheckConstraint("CK_Araclar_Km", SchemaLimits.MileageCheck);
        });

        builder.HasKey(c => c.Id).HasName("PK_Araclar");
        builder.Property(c => c.Id).HasColumnName("AracID").ValueGeneratedOnAdd();
        builder.Property(c => c.Brand).HasColumnName("Marka").HasMaxLength(SchemaLimits.BrandLength).IsRequired();
        builder.Property(c => c.Model).HasColumnName("Model").HasMaxLength(SchemaLimits.ModelLength).IsRequired();
        builder.Property(c => c.Year).HasColumnName("Yil");
        builder.Property(c => c.Transmission).HasColumnName("VitesTipi").HasMaxLength(SchemaLimits.TransmissionLength).IsRequired();
        builder.Property(c => c.FuelType).HasColumnName("YakitTipi").HasMaxLength(SchemaLimits.FuelTypeLength).IsRequired();
        builder.Property(c => c.Mileage).HasColumnName("Km");
        builder.Property(c => c.Color).HasColumnName("Renk").HasMaxLength(SchemaLimits.ColorLength).IsRequired();
        builder.Property(c => c.Plate).HasColumnName("Plaka").HasMaxLength(SchemaLimits.PlateLength).IsRequired();
        builder.Property(c => c.DailyPrice).HasColumnName("GunlukUcret").HasPrecision(SchemaLimits.MoneyPrecision, SchemaLimits.MoneyScale);
        builder.Property(c => c.ImageUrl).HasColumnName("Image").HasMaxLength(SchemaLimits.ImageUrlLength).IsUnicode(false);
        builder.Property(c => c.IsListed).HasColumnName("KiralamayaAcik");

        builder.HasIndex(c => c.Plate, "IX_Araclar_Plaka").IsUnique();
        builder.HasIndex(c => c.Brand, "IX_Araclar_Marka");
    }
}
