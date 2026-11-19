using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AracKiralama.Data.Persistence.Migrations
{
    /// <summary>
    /// Adds the edit version of the accounts (DuzenlemeSurumu), which every edit of an account raises, so
    /// an admin's form loaded before another edit cannot write over it; the session version keeps its own
    /// job of ending sessions. Existing accounts start at 0. The default constraint is named, so an upgraded
    /// legacy database and a new one end up with the same schema.
    /// </summary>
    public partial class AddAccountEditVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE [Kullanicilar] ADD [DuzenlemeSurumu] int NOT NULL CONSTRAINT [DF_Kullanicilar_DuzenlemeSurumu] DEFAULT 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE [Kullanicilar] DROP CONSTRAINT [DF_Kullanicilar_DuzenlemeSurumu];");
            migrationBuilder.DropColumn(
                name: "DuzenlemeSurumu",
                table: "Kullanicilar");
        }
    }
}
