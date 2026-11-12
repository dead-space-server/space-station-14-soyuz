// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class SoyuzCreditsInit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "soyuz_credit_balance",
                columns: table => new
                {
                    server_id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    balance = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_soyuz_credit_balance", x => new { x.server_id, x.user_id });
                    table.CheckConstraint("ck_soyuz_credit_balance_nonneg", "balance >= 0");
                });

            migrationBuilder.CreateTable(
                name: "soyuz_credit_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    server_id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    amount = table.Column<long>(type: "INTEGER", nullable: false),
                    type = table.Column<int>(type: "INTEGER", nullable: false),
                    reason = table.Column<string>(type: "TEXT", nullable: false),
                    ref_id = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_soyuz_credit_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "soyuz_credit_owned",
                columns: table => new
                {
                    server_id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    product_id = table.Column<string>(type: "TEXT", nullable: false),
                    equipped = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_soyuz_credit_owned", x => new { x.server_id, x.user_id, x.product_id });
                });

            migrationBuilder.CreateTable(
                name: "soyuz_credit_playtime",
                columns: table => new
                {
                    server_id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    seconds = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_soyuz_credit_playtime", x => new { x.server_id, x.user_id });
                });

            migrationBuilder.CreateTable(
                name: "soyuz_credit_purchase",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    server_id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    product_id = table.Column<string>(type: "TEXT", nullable: false),
                    price = table.Column<long>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_soyuz_credit_purchase", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "soyuz_credit_referral",
                columns: table => new
                {
                    server_id = table.Column<string>(type: "TEXT", nullable: false),
                    invitee_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    inviter_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    code = table.Column<string>(type: "TEXT", nullable: false),
                    applied_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    rewarded_at = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_soyuz_credit_referral", x => new { x.server_id, x.invitee_id });
                });

            migrationBuilder.CreateTable(
                name: "soyuz_credit_referral_code",
                columns: table => new
                {
                    server_id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    code = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_soyuz_credit_referral_code", x => new { x.server_id, x.user_id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_soyuz_credit_log_server_id_user_id_id",
                table: "soyuz_credit_log",
                columns: new[] { "server_id", "user_id", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_soyuz_credit_purchase_server_id_user_id",
                table: "soyuz_credit_purchase",
                columns: new[] { "server_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_soyuz_credit_referral_server_id_inviter_id",
                table: "soyuz_credit_referral",
                columns: new[] { "server_id", "inviter_id" });

            migrationBuilder.CreateIndex(
                name: "IX_soyuz_credit_referral_code_server_id_code",
                table: "soyuz_credit_referral_code",
                columns: new[] { "server_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "soyuz_credit_balance");

            migrationBuilder.DropTable(
                name: "soyuz_credit_log");

            migrationBuilder.DropTable(
                name: "soyuz_credit_owned");

            migrationBuilder.DropTable(
                name: "soyuz_credit_playtime");

            migrationBuilder.DropTable(
                name: "soyuz_credit_purchase");

            migrationBuilder.DropTable(
                name: "soyuz_credit_referral");

            migrationBuilder.DropTable(
                name: "soyuz_credit_referral_code");
        }
    }
}
