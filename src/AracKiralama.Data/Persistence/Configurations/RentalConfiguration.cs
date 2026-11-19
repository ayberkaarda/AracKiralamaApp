using AracKiralama.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AracKiralama.Data.Persistence.Configurations;

internal sealed class RentalConfiguration : IEntityTypeConfiguration<Rental>
{
    public void Configure(EntityTypeBuilder<Rental> builder)
    {
        builder.ToTable("Kiralama", table =>
        {
            table.HasCheckConstraint("CK_Kiralama_Durum", SchemaLimits.RentalStatusCheck);
            table.HasCheckConstraint("CK_Kiralama_Iade", SchemaLimits.RentalReturnCheck);
            table.HasCheckConstraint("CK_Kiralama_Tarih", SchemaLimits.RentalDatesCheck);
            table.HasCheckConstraint("CK_Kiralama_Ucret", SchemaLimits.RentalPriceCheck);
        });

        builder.HasKey(r => r.Id).HasName("PK_Kiralama");
        builder.Property(r => r.Id).HasColumnName("KiralamaID").ValueGeneratedOnAdd();
        builder.Property(r => r.CarId).HasColumnName("AracID");
        builder.Property(r => r.UserId).HasColumnName("KullaniciID");
        builder.Property(r => r.StartDate).HasColumnName("AlisTarihi");
        builder.Property(r => r.EndDate).HasColumnName("IadeTarihi");
        builder.Property(r => r.ReturnedOn).HasColumnName("GercekIadeTarihi");
        builder.Property(r => r.Status).HasColumnName("Durum");
        builder.Property(r => r.Price).HasColumnName("Ucret").HasPrecision(SchemaLimits.MoneyPrecision, SchemaLimits.MoneyScale);
        builder.Ignore(r => r.Days);

        // Rentals keep cars and accounts: neither can be deleted while it has rental history.
        builder.HasOne<Car>().WithMany().HasForeignKey(r => r.CarId).HasConstraintName("FK_Kiralama_Araclar").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.UserId).HasConstraintName("FK_Kiralama_Kullanicilar").OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(r => r.CarId, "IX_Kiralama_AracID");
        builder.HasIndex(r => r.UserId, "IX_Kiralama_KullaniciID");
        builder.HasIndex(r => new { r.CarId, r.StartDate }, "IX_Kiralama_AracID_AlisTarihi").IsUnique().HasFilter(SchemaLimits.ActiveRentalFilter);
    }
}
