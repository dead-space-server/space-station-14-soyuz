using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddProfileHeight : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // DS14-height-start
            migrationBuilder.AddColumn<int>(
                name: "height",
                table: "profile",
                type: "INTEGER",
                nullable: false,
                defaultValue: 175);
            // DS14-height-end
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // DS14-height-start
            migrationBuilder.DropColumn(
                name: "height",
                table: "profile");
            // DS14-height-end
        }
    }
}
