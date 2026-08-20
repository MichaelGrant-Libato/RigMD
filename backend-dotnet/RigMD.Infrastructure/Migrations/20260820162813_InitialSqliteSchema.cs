using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RigMD.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialSqliteSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SystemProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CpuModel = table.Column<string>(type: "TEXT", nullable: false),
                    RamCapacity = table.Column<string>(type: "TEXT", nullable: false),
                    StorageType = table.Column<string>(type: "TEXT", nullable: false),
                    StorageCapacity = table.Column<string>(type: "TEXT", nullable: false),
                    StorageDetails = table.Column<string>(type: "TEXT", nullable: true),
                    OsVersion = table.Column<string>(type: "TEXT", nullable: false),
                    GpuDriver = table.Column<string>(type: "TEXT", nullable: true),
                    ChipsetDriver = table.Column<string>(type: "TEXT", nullable: true),
                    SystemAge = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WarningSigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    DefaultThreshold = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WarningSigns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DiagnosticSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SystemProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiagnosticSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiagnosticSessions_SystemProfiles_SystemProfileId",
                        column: x => x.SystemProfileId,
                        principalTable: "SystemProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DiagnosticOutputs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DiagnosticSessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DiagnosedCategory = table.Column<string>(type: "TEXT", nullable: false),
                    ActionCategory = table.Column<string>(type: "TEXT", nullable: false),
                    ConfidenceLabel = table.Column<string>(type: "TEXT", nullable: false),
                    AiExplanation = table.Column<string>(type: "TEXT", nullable: true),
                    ReasoningFactors = table.Column<string>(type: "TEXT", nullable: false),
                    WarningSigns = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiagnosticOutputs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiagnosticOutputs_DiagnosticSessions_DiagnosticSessionId",
                        column: x => x.DiagnosticSessionId,
                        principalTable: "DiagnosticSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SessionAnswers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DiagnosticSessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuestionKey = table.Column<string>(type: "TEXT", nullable: false),
                    AnswerValue = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionAnswers_DiagnosticSessions_DiagnosticSessionId",
                        column: x => x.DiagnosticSessionId,
                        principalTable: "DiagnosticSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RemediationRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DiagnosticOutputId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemediationRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RemediationRuns_DiagnosticOutputs_DiagnosticOutputId",
                        column: x => x.DiagnosticOutputId,
                        principalTable: "DiagnosticOutputs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ActionAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RemediationRunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActionCode = table.Column<string>(type: "TEXT", nullable: false),
                    PreconditionState = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActionAttempts_RemediationRuns_RemediationRunId",
                        column: x => x.RemediationRunId,
                        principalTable: "RemediationRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PivotEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RemediationRunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    FromActionCode = table.Column<string>(type: "TEXT", nullable: false),
                    ToActionCode = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PivotEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PivotEvents_RemediationRuns_RemediationRunId",
                        column: x => x.RemediationRunId,
                        principalTable: "RemediationRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RollbackEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RemediationRunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActionAttemptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WasSuccessful = table.Column<bool>(type: "INTEGER", nullable: false),
                    RestoredState = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RollbackEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RollbackEvents_ActionAttempts_ActionAttemptId",
                        column: x => x.ActionAttemptId,
                        principalTable: "ActionAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RollbackEvents_RemediationRuns_RemediationRunId",
                        column: x => x.RemediationRunId,
                        principalTable: "RemediationRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VerificationResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActionAttemptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsSuccessful = table.Column<bool>(type: "INTEGER", nullable: false),
                    ObservedState = table.Column<string>(type: "TEXT", nullable: false),
                    FailureReason = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerificationResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VerificationResults_ActionAttempts_ActionAttemptId",
                        column: x => x.ActionAttemptId,
                        principalTable: "ActionAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActionAttempts_RemediationRunId",
                table: "ActionAttempts",
                column: "RemediationRunId");

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticOutputs_DiagnosticSessionId",
                table: "DiagnosticOutputs",
                column: "DiagnosticSessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticSessions_SystemProfileId",
                table: "DiagnosticSessions",
                column: "SystemProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_PivotEvents_RemediationRunId",
                table: "PivotEvents",
                column: "RemediationRunId");

            migrationBuilder.CreateIndex(
                name: "IX_RemediationRuns_DiagnosticOutputId",
                table: "RemediationRuns",
                column: "DiagnosticOutputId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RollbackEvents_ActionAttemptId",
                table: "RollbackEvents",
                column: "ActionAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_RollbackEvents_RemediationRunId",
                table: "RollbackEvents",
                column: "RemediationRunId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionAnswers_DiagnosticSessionId",
                table: "SessionAnswers",
                column: "DiagnosticSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_VerificationResults_ActionAttemptId",
                table: "VerificationResults",
                column: "ActionAttemptId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PivotEvents");

            migrationBuilder.DropTable(
                name: "RollbackEvents");

            migrationBuilder.DropTable(
                name: "SessionAnswers");

            migrationBuilder.DropTable(
                name: "VerificationResults");

            migrationBuilder.DropTable(
                name: "WarningSigns");

            migrationBuilder.DropTable(
                name: "ActionAttempts");

            migrationBuilder.DropTable(
                name: "RemediationRuns");

            migrationBuilder.DropTable(
                name: "DiagnosticOutputs");

            migrationBuilder.DropTable(
                name: "DiagnosticSessions");

            migrationBuilder.DropTable(
                name: "SystemProfiles");
        }
    }
}
