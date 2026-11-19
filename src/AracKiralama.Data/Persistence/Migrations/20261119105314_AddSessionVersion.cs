using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AracKiralama.Data.Persistence.Migrations
{
    /// <summary>
    /// Adds the session version of the accounts (OturumSurumu). Existing accounts start at 0, the value
    /// every session issued before the upgrade carries. The default constraint is named, so an upgraded
    /// legacy database and a new one end up with the same schema.
    /// </summary>
    public partial class AddSessionVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE [Kullanicilar] ADD [OturumSurumu] int NOT NULL CONSTRAINT [DF_Kullanicilar_OturumSurumu] DEFAULT 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE [Kullanicilar] DROP CONSTRAINT [DF_Kullanicilar_OturumSurumu];");
            migrationBuilder.DropColumn(
                name: "OturumSurumu",
                table: "Kullanicilar");
        }
    }
}
