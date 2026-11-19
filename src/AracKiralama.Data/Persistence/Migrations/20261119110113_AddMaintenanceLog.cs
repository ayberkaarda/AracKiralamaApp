using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AracKiralama.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenanceLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AracBakim",
                columns: table => new
                {
                    BakimID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AracID = table.Column<int>(type: "int", nullable: false),
                    Tur = table.Column<int>(type: "int", nullable: false),
                    Aciklama = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    BaslangicTarihi = table.Column<DateOnly>(type: "date", nullable: false),
                    BitisTarihi = table.Column<DateOnly>(type: "date", nullable: false),
                    Maliyet = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    Durum = table.Column<int>(type: "int", nullable: false),
                    OlusturmaZamani = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GuncellemeZamani = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Surum = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AracBakim", x => x.BakimID);
                    table.CheckConstraint("CK_AracBakim_Durum", "[Durum] IN (0, 1)");
                    table.CheckConstraint("CK_AracBakim_Maliyet", "[Maliyet] >= 0");
                    table.CheckConstraint("CK_AracBakim_Surum", "[Surum] >= 1");
                    table.CheckConstraint("CK_AracBakim_Tarih", "[BitisTarihi] > [BaslangicTarihi]");
                    table.CheckConstraint("CK_AracBakim_Tur", "[Tur] IN (0, 1)");
                    table.ForeignKey(
                        name: "FK_AracBakim_Araclar",
                        column: x => x.AracID,
                        principalTable: "Araclar",
                        principalColumn: "AracID");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AracBakim_AracID_BaslangicTarihi",
                table: "AracBakim",
                columns: new[] { "AracID", "BaslangicTarihi" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AracBakim");
        }
    }
}
