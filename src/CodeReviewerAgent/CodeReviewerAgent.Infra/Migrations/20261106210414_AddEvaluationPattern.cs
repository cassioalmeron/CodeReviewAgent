using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodeReviewerAgent.Infra.Migrations
{
    /// <inheritdoc />
    public partial class AddEvaluationPattern : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Pattern",
                table: "Evaluation",
                type: "text",
                nullable: false,
                defaultValue: "single");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Pattern",
                table: "Evaluation");
        }
    }
}
