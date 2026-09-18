using System;
using Microsoft.EntityFrameworkCore.Migrations;
using CodeReviewerAgent.Infra.Migrations.ProviderTypes;

#nullable disable

namespace CodeReviewerAgent.Infra.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Postgres in use, SQLite in the tests: every provider-specific type comes from here.
            var providerTypes = MigrationProviderTypes.For(migrationBuilder.ActiveProvider);

            migrationBuilder.CreateTable(
                name: "GoldenRun",
                columns: table => new
                {
                    Id = providerTypes.AsAutoIncrementPrimaryKey(table.Column<int>(nullable: false)),
                    Model = table.Column<string>(nullable: false),
                    Engine = table.Column<string>(nullable: true),
                    Skills = table.Column<string>(nullable: true),
                    PromptVersion = table.Column<string>(nullable: true),
                    StartedAt = table.Column<DateTime>(type: providerTypes.DateTime, nullable: false),
                    DurationMs = table.Column<long>(nullable: false),
                    Cost = table.Column<decimal>(precision: 18, scale: 8, nullable: false),
                    InputTokens = table.Column<int>(nullable: false),
                    OutputTokens = table.Column<int>(nullable: false),
                    LatencyP50Ms = table.Column<long>(nullable: false),
                    LatencyP95Ms = table.Column<long>(nullable: false),
                    LatencyP99Ms = table.Column<long>(nullable: false),
                    ReportFile = table.Column<string>(nullable: true),
                    Approved = table.Column<bool>(type: providerTypes.Bool, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoldenRun", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Project",
                columns: table => new
                {
                    Id = providerTypes.AsAutoIncrementPrimaryKey(table.Column<int>(nullable: false)),
                    Name = table.Column<string>(nullable: false),
                    Folder = table.Column<string>(nullable: false),
                    CreatedAt = table.Column<DateTime>(type: providerTypes.DateTime, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Project", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GoldenCaseResult",
                columns: table => new
                {
                    Id = providerTypes.AsAutoIncrementPrimaryKey(table.Column<int>(nullable: false)),
                    RunId = table.Column<int>(nullable: false),
                    CaseName = table.Column<string>(nullable: false),
                    Kind = table.Column<int>(nullable: false),
                    Runs = table.Column<int>(nullable: false),
                    Successes = table.Column<int>(nullable: false),
                    CleanRounds = table.Column<int>(nullable: false),
                    PrecisionCorrect = table.Column<int>(nullable: false),
                    PrecisionCounted = table.Column<int>(nullable: false),
                    ExactCalibrations = table.Column<int>(nullable: false),
                    DiscardedFindings = table.Column<int>(nullable: false),
                    Approved = table.Column<bool>(type: providerTypes.Bool, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoldenCaseResult", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GoldenCaseResult_GoldenRun_RunId",
                        column: x => x.RunId,
                        principalTable: "GoldenRun",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GoldenGate",
                columns: table => new
                {
                    Id = providerTypes.AsAutoIncrementPrimaryKey(table.Column<int>(nullable: false)),
                    RunId = table.Column<int>(nullable: false),
                    Name = table.Column<string>(nullable: false),
                    Part = table.Column<int>(nullable: false),
                    Whole = table.Column<int>(nullable: false),
                    Floor = table.Column<int>(nullable: false),
                    Direction = table.Column<int>(nullable: false),
                    Passed = table.Column<bool>(type: providerTypes.Bool, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoldenGate", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GoldenGate_GoldenRun_RunId",
                        column: x => x.RunId,
                        principalTable: "GoldenRun",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Review",
                columns: table => new
                {
                    Id = providerTypes.AsAutoIncrementPrimaryKey(table.Column<int>(nullable: false)),
                    ProjectId = table.Column<int>(nullable: false),
                    Content = table.Column<string>(nullable: false),
                    ContentHash = table.Column<string>(nullable: false),
                    Source = table.Column<string>(nullable: true),
                    CreatedAt = table.Column<DateTime>(type: providerTypes.DateTime, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Review", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Review_Project_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Project",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Assessment",
                columns: table => new
                {
                    Id = providerTypes.AsAutoIncrementPrimaryKey(table.Column<int>(nullable: false)),
                    ReviewId = table.Column<int>(nullable: false),
                    Summary = table.Column<string>(nullable: true),
                    Engine = table.Column<string>(nullable: true),
                    Model = table.Column<string>(nullable: true),
                    PromptVersion = table.Column<string>(nullable: true),
                    Skills = table.Column<string>(nullable: true),
                    Cost = table.Column<decimal>(precision: 18, scale: 8, nullable: false),
                    LatencyMs = table.Column<long>(nullable: false),
                    InputTokens = table.Column<int>(nullable: false),
                    OutputTokens = table.Column<int>(nullable: false),
                    DiscardedFindings = table.Column<int>(nullable: false),
                    RunId = table.Column<int>(nullable: true),
                    CreatedAt = table.Column<DateTime>(type: providerTypes.DateTime, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assessment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Assessment_GoldenRun_RunId",
                        column: x => x.RunId,
                        principalTable: "GoldenRun",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Assessment_Review_ReviewId",
                        column: x => x.ReviewId,
                        principalTable: "Review",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Evaluation",
                columns: table => new
                {
                    Id = providerTypes.AsAutoIncrementPrimaryKey(table.Column<int>(nullable: false)),
                    AssessmentId = table.Column<int>(nullable: false),
                    RubricVersion = table.Column<string>(nullable: true),
                    JudgeModel = table.Column<string>(nullable: true),
                    Correctness = table.Column<int>(nullable: false),
                    Actionability = table.Column<int>(nullable: false),
                    Calibration = table.Column<int>(nullable: false),
                    SignalToNoise = table.Column<int>(nullable: false),
                    Overall = table.Column<int>(nullable: false),
                    Rationale = table.Column<string>(nullable: true),
                    Cost = table.Column<decimal>(precision: 18, scale: 8, nullable: false),
                    LatencyMs = table.Column<long>(nullable: false),
                    InputTokens = table.Column<int>(nullable: false),
                    OutputTokens = table.Column<int>(nullable: false),
                    CreatedAt = table.Column<DateTime>(type: providerTypes.DateTime, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Evaluation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Evaluation_Assessment_AssessmentId",
                        column: x => x.AssessmentId,
                        principalTable: "Assessment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Finding",
                columns: table => new
                {
                    Id = providerTypes.AsAutoIncrementPrimaryKey(table.Column<int>(nullable: false)),
                    File = table.Column<string>(nullable: true),
                    CodeSnippet = table.Column<string>(nullable: true),
                    Severity = table.Column<int>(nullable: true),
                    Category = table.Column<int>(nullable: true),
                    Problem = table.Column<string>(nullable: true),
                    Suggestion = table.Column<string>(nullable: true),
                    Line = table.Column<int>(nullable: true),
                    AssessmentId = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Finding", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Finding_Assessment_AssessmentId",
                        column: x => x.AssessmentId,
                        principalTable: "Assessment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Assessment_ReviewId",
                table: "Assessment",
                column: "ReviewId");

            migrationBuilder.CreateIndex(
                name: "IX_Assessment_RunId",
                table: "Assessment",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_Evaluation_AssessmentId",
                table: "Evaluation",
                column: "AssessmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Finding_AssessmentId",
                table: "Finding",
                column: "AssessmentId");

            migrationBuilder.CreateIndex(
                name: "IX_GoldenCaseResult_RunId_CaseName",
                table: "GoldenCaseResult",
                columns: new[] { "RunId", "CaseName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GoldenGate_RunId_Name",
                table: "GoldenGate",
                columns: new[] { "RunId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GoldenRun_Model_Skills_PromptVersion_StartedAt",
                table: "GoldenRun",
                columns: new[] { "Model", "Skills", "PromptVersion", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Project_Folder",
                table: "Project",
                column: "Folder",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Review_ProjectId_ContentHash",
                table: "Review",
                columns: new[] { "ProjectId", "ContentHash" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Evaluation");

            migrationBuilder.DropTable(
                name: "Finding");

            migrationBuilder.DropTable(
                name: "GoldenCaseResult");

            migrationBuilder.DropTable(
                name: "GoldenGate");

            migrationBuilder.DropTable(
                name: "Assessment");

            migrationBuilder.DropTable(
                name: "GoldenRun");

            migrationBuilder.DropTable(
                name: "Review");

            migrationBuilder.DropTable(
                name: "Project");
        }
    }
}
