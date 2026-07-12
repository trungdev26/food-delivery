using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodDelivery.Api.Migrations
{
    public partial class InitialCreate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "categories",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Icon = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_categories", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "categories",
                columns: new[] { "Id", "Icon", "Name" },
                values: new object[,]
                {
                    { "an-vat", "🍢", "Ăn vặt" },
                    { "com", "🍚", "Cơm" },
                    { "do-chay", "🥗", "Đồ chay" },
                    { "ga-ran", "🍗", "Gà rán" },
                    { "pho-bun", "🍜", "Phở & Bún" },
                    { "pizza", "🍕", "Pizza" },
                    { "tra-sua", "🧋", "Trà sữa" },
                    { "trang-mieng", "🍰", "Tráng miệng" }
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "categories");
        }
    }
}
