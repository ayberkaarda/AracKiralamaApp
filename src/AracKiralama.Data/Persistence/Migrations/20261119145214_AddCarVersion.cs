using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AracKiralama.Data.Persistence.Migrations
{
    /// <summary>
    /// Adds the version of the cars (Surum), which every change of a car's details raises, so a form
    /// loaded before another admin's change cannot write over it. Existing cars start at 1, like a new
    /// one. The default constraint is named, so an upgraded legacy database and a new one end up with the
    /// same schema.
    /// </summary>
    public partial class AddCarVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE [Araclar] ADD [Surum] int NOT NULL CONSTRAINT [DF_Araclar_Surum] DEFAULT 1;");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Araclar_Surum",
                table: "Araclar",
                sql: "[Surum] >= 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Araclar_Surum",
                table: "Araclar");

            migrationBuilder.Sql("ALTER TABLE [Araclar] DROP CONSTRAINT [DF_Araclar_Surum];");
            migrationBuilder.DropColumn(
                name: "Surum",
                table: "Araclar");
        }
    }
}
