using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodeReviewerAgent.Infra.Migrations
{
    /// <inheritdoc />
    public partial class AddGoldenRunPattern : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Pattern",
                table: "GoldenRun",
                nullable: false,
                defaultValue: "single");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Pattern",
                table: "GoldenRun");
        }
    }
}
