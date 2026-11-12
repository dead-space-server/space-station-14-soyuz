// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Database.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class SoyuzCreditsPromoRefunds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "rejected_at",
                table: "soyuz_credit_referral",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "refunded_at",
                table: "soyuz_credit_purchase",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "soyuz_credit_discord_link",
                columns: table => new
                {
                    server_id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    discord_id = table.Column<string>(type: "text", nullable: false),
                    linked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_soyuz_credit_discord_link", x => new { x.server_id, x.user_id });
                });

            migrationBuilder.CreateTable(
                name: "soyuz_credit_promo",
                columns: table => new
                {
                    server_id = table.Column<string>(type: "text", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    credits = table.Column<long>(type: "bigint", nullable: false),
                    product_id = table.Column<string>(type: "text", nullable: true),
                    max_uses = table.Column<int>(type: "integer", nullable: true),
                    uses = table.Column<int>(type: "integer", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    disabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_soyuz_credit_promo", x => new { x.server_id, x.code });
                });

            migrationBuilder.CreateTable(
                name: "soyuz_credit_promo_use",
                columns: table => new
                {
                    server_id = table.Column<string>(type: "text", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    used_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_soyuz_credit_promo_use", x => new { x.server_id, x.code, x.user_id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_soyuz_credit_discord_link_server_id_discord_id",
                table: "soyuz_credit_discord_link",
                columns: new[] { "server_id", "discord_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "soyuz_credit_discord_link");

            migrationBuilder.DropTable(
                name: "soyuz_credit_promo");

            migrationBuilder.DropTable(
                name: "soyuz_credit_promo_use");

            migrationBuilder.DropColumn(
                name: "rejected_at",
                table: "soyuz_credit_referral");

            migrationBuilder.DropColumn(
                name: "refunded_at",
                table: "soyuz_credit_purchase");
        }
    }
}
