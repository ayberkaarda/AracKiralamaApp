using AracKiralama.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AracKiralama.Data.Persistence.Configurations;

/// <summary>AracBakim: the maintenance and damage log of the cars. docs/grant-app-login.sql gives no DELETE on it.</summary>
internal sealed class MaintenanceConfiguration : IEntityTypeConfiguration<MaintenanceRecord>
{
    public void Configure(EntityTypeBuilder<MaintenanceRecord> builder)
    {
        builder.ToTable("AracBakim", table =>
        {
            table.HasCheckConstraint("CK_AracBakim_Tur", SchemaLimits.MaintenanceKindCheck);
            table.HasCheckConstraint("CK_AracBakim_Durum", SchemaLimits.MaintenanceStatusCheck);
            table.HasCheckConstraint("CK_AracBakim_Tarih", SchemaLimits.MaintenanceDatesCheck);
            table.HasCheckConstraint("CK_AracBakim_Maliyet", SchemaLimits.MaintenanceCostCheck);
            table.HasCheckConstraint("CK_AracBakim_Surum", SchemaLimits.MaintenanceVersionCheck);
        });

        builder.HasKey(m => m.Id).HasName("PK_AracBakim");
        builder.Property(m => m.Id).HasColumnName("BakimID").ValueGeneratedOnAdd();
        builder.Property(m => m.CarId).HasColumnName("AracID");
        builder.Property(m => m.Kind).HasColumnName("Tur");
        builder.Property(m => m.Description).HasColumnName("Aciklama").HasMaxLength(SchemaLimits.MaintenanceDescriptionLength).IsRequired();
        builder.Property(m => m.StartDate).HasColumnName("BaslangicTarihi");
        builder.Property(m => m.EndDate).HasColumnName("BitisTarihi");
        builder.Property(m => m.Cost).HasColumnName("Maliyet").HasPrecision(SchemaLimits.MoneyPrecision, SchemaLimits.MoneyScale);
        builder.Property(m => m.Status).HasColumnName("Durum");
        builder.Property(m => m.CreatedAt).HasColumnName("OlusturmaZamani");
        builder.Property(m => m.UpdatedAt).HasColumnName("GuncellemeZamani");
        builder.Property(m => m.Version).HasColumnName("Surum");

        // The log keeps the car: a car with maintenance history cannot be deleted.
        builder.HasOne<Car>().WithMany().HasForeignKey(m => m.CarId).HasConstraintName("FK_AracBakim_Araclar").OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(m => new { m.CarId, m.StartDate }, "IX_AracBakim_AracID_BaslangicTarihi");
    }
}
