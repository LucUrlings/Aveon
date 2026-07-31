using backend.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260731185000_AddDefaultReturnRankingToUser")]
public partial class AddDefaultReturnRankingToUser : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "DefaultReturnRanking",
            table: "AspNetUsers",
            type: "text",
            nullable: false,
            defaultValue: "best");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "DefaultReturnRanking",
            table: "AspNetUsers");
    }
}
