using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace albiondata_sql_dotNet.Migrations
{
    /// <inheritdoc />
    public partial class initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gold_prices",
                columns: table => new
                {
                    id = table.Column<decimal>(type: "decimal(20,0)", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    price = table.Column<long>(type: "bigint", nullable: false),
                    timestamp = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gold_prices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "market_history",
                columns: table => new
                {
                    id = table.Column<decimal>(type: "decimal(20,0)", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    item_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    location = table.Column<int>(type: "int", nullable: false),
                    quality = table.Column<byte>(type: "tinyint", nullable: false),
                    timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    aggregation = table.Column<int>(type: "int", nullable: false),
                    item_amount = table.Column<decimal>(type: "decimal(20,0)", nullable: false),
                    silver_amount = table.Column<decimal>(type: "decimal(20,0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_market_history", x => x.id);
                    table.UniqueConstraint("Main", x => new { x.item_id, x.quality, x.location, x.timestamp, x.aggregation });
                });

            migrationBuilder.CreateTable(
                name: "market_orders",
                columns: table => new
                {
                    id = table.Column<decimal>(type: "decimal(20,0)", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    albion_id = table.Column<decimal>(type: "decimal(20,0)", nullable: false),
                    initial_amount = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    item_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    location = table.Column<int>(type: "int", nullable: false),
                    quality_level = table.Column<byte>(type: "tinyint", nullable: false),
                    enchantment_level = table.Column<byte>(type: "tinyint", nullable: false),
                    price = table.Column<decimal>(type: "decimal(20,0)", nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    auction_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    expires = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_market_orders", x => x.id);
                    table.UniqueConstraint("AlbionId", x => x.albion_id);
                });

            migrationBuilder.CreateTable(
                name: "market_orders_expired",
                columns: table => new
                {
                    id = table.Column<decimal>(type: "decimal(20,0)", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    item_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    location = table.Column<int>(type: "int", nullable: false),
                    quality_level = table.Column<byte>(type: "tinyint", nullable: false),
                    enchantment_level = table.Column<byte>(type: "tinyint", nullable: false),
                    price = table.Column<decimal>(type: "decimal(20,0)", nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    auction_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    expires = table.Column<DateTime>(type: "datetime2", nullable: false),
                    albion_id = table.Column<decimal>(type: "decimal(20,0)", nullable: false),
                    initial_amount = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_market_orders_expired", x => x.id);
                    table.UniqueConstraint("AlbionId1", x => x.albion_id);
                });

            migrationBuilder.CreateTable(
                name: "market_stats",
                columns: table => new
                {
                    id = table.Column<decimal>(type: "decimal(20,0)", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    item_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    location = table.Column<int>(type: "int", nullable: false),
                    price_avg = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    price_max = table.Column<decimal>(type: "decimal(20,0)", nullable: false),
                    price_min = table.Column<decimal>(type: "decimal(20,0)", nullable: false),
                    timestamp = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_market_stats", x => x.id);
                    table.UniqueConstraint("AK_market_stats_item_id_location_timestamp", x => new { x.item_id, x.location, x.timestamp });
                });

            migrationBuilder.CreateIndex(
                name: "IX_gold_prices_timestamp",
                table: "gold_prices",
                column: "timestamp");

            migrationBuilder.CreateIndex(
                name: "Deleted",
                table: "market_orders",
                column: "deleted_at");

            migrationBuilder.CreateIndex(
                name: "Expired",
                table: "market_orders",
                columns: new[] { "deleted_at", "expires", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "Main",
                table: "market_orders",
                columns: new[] { "item_id", "auction_type", "location", "updated_at", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "TypeId",
                table: "market_orders",
                columns: new[] { "item_id", "updated_at", "deleted_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gold_prices");

            migrationBuilder.DropTable(
                name: "market_history");

            migrationBuilder.DropTable(
                name: "market_orders");

            migrationBuilder.DropTable(
                name: "market_orders_expired");

            migrationBuilder.DropTable(
                name: "market_stats");
        }
    }
}
