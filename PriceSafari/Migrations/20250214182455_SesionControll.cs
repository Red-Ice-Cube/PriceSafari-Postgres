using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class SesionControll : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DaySessionStores");

            migrationBuilder.DropTable(
                name: "DaySessions");

            migrationBuilder.DropTable(
                name: "DayPlans");

            migrationBuilder.DropTable(
                name: "WeeklyPlans");

            migrationBuilder.CreateTable(
                name: "DayDetails",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DayDetails", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SchedulePlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MondayId = table.Column<int>(type: "int", nullable: true),
                    TuesdayId = table.Column<int>(type: "int", nullable: true),
                    WednesdayId = table.Column<int>(type: "int", nullable: true),
                    ThursdayId = table.Column<int>(type: "int", nullable: true),
                    FridayId = table.Column<int>(type: "int", nullable: true),
                    SaturdayId = table.Column<int>(type: "int", nullable: true),
                    SundayId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchedulePlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SchedulePlans_DayDetails_FridayId",
                        column: x => x.FridayId,
                        principalTable: "DayDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SchedulePlans_DayDetails_MondayId",
                        column: x => x.MondayId,
                        principalTable: "DayDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SchedulePlans_DayDetails_SaturdayId",
                        column: x => x.SaturdayId,
                        principalTable: "DayDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SchedulePlans_DayDetails_SundayId",
                        column: x => x.SundayId,
                        principalTable: "DayDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SchedulePlans_DayDetails_ThursdayId",
                        column: x => x.ThursdayId,
                        principalTable: "DayDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SchedulePlans_DayDetails_TuesdayId",
                        column: x => x.TuesdayId,
                        principalTable: "DayDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SchedulePlans_DayDetails_WednesdayId",
                        column: x => x.WednesdayId,
                        principalTable: "DayDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StartTime = table.Column<TimeSpan>(type: "time", nullable: false),
                    BaseEnabled = table.Column<bool>(type: "bit", nullable: false),
                    UrlEnabled = table.Column<bool>(type: "bit", nullable: false),
                    GoogleEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CeneoEnabled = table.Column<bool>(type: "bit", nullable: false),
                    DayDetailId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduleTasks_DayDetails_DayDetailId",
                        column: x => x.DayDetailId,
                        principalTable: "DayDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleTaskStores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ScheduleTaskId = table.Column<int>(type: "int", nullable: false),
                    StoreId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleTaskStores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduleTaskStores_ScheduleTasks_ScheduleTaskId",
                        column: x => x.ScheduleTaskId,
                        principalTable: "ScheduleTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScheduleTaskStores_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "StoreId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SchedulePlans_FridayId",
                table: "SchedulePlans",
                column: "FridayId",
                unique: true,
                filter: "[FridayId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SchedulePlans_MondayId",
                table: "SchedulePlans",
                column: "MondayId",
                unique: true,
                filter: "[MondayId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SchedulePlans_SaturdayId",
                table: "SchedulePlans",
                column: "SaturdayId",
                unique: true,
                filter: "[SaturdayId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SchedulePlans_SundayId",
                table: "SchedulePlans",
                column: "SundayId",
                unique: true,
                filter: "[SundayId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SchedulePlans_ThursdayId",
                table: "SchedulePlans",
                column: "ThursdayId",
                unique: true,
                filter: "[ThursdayId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SchedulePlans_TuesdayId",
                table: "SchedulePlans",
                column: "TuesdayId",
                unique: true,
                filter: "[TuesdayId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SchedulePlans_WednesdayId",
                table: "SchedulePlans",
                column: "WednesdayId",
                unique: true,
                filter: "[WednesdayId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleTasks_DayDetailId",
                table: "ScheduleTasks",
                column: "DayDetailId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleTaskStores_ScheduleTaskId",
                table: "ScheduleTaskStores",
                column: "ScheduleTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleTaskStores_StoreId",
                table: "ScheduleTaskStores",
                column: "StoreId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SchedulePlans");

            migrationBuilder.DropTable(
                name: "ScheduleTaskStores");

            migrationBuilder.DropTable(
                name: "ScheduleTasks");

            migrationBuilder.DropTable(
                name: "DayDetails");

            migrationBuilder.CreateTable(
                name: "WeeklyPlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlanName = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeeklyPlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DayPlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WeeklyPlanId = table.Column<int>(type: "int", nullable: false),
                    DayOfWeek = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DayPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DayPlans_WeeklyPlans_WeeklyPlanId",
                        column: x => x.WeeklyPlanId,
                        principalTable: "WeeklyPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DaySessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DayPlanId = table.Column<int>(type: "int", nullable: false),
                    BaseScalEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CeneoScraperEnabled = table.Column<bool>(type: "bit", nullable: false),
                    GoogleScraperEnabled = table.Column<bool>(type: "bit", nullable: false),
                    StartTime = table.Column<TimeSpan>(type: "time", nullable: false),
                    UrlScalEnabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DaySessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DaySessions_DayPlans_DayPlanId",
                        column: x => x.DayPlanId,
                        principalTable: "DayPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DaySessionStores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DaySessionId = table.Column<int>(type: "int", nullable: false),
                    StoreId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DaySessionStores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DaySessionStores_DaySessions_DaySessionId",
                        column: x => x.DaySessionId,
                        principalTable: "DaySessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DaySessionStores_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "StoreId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DayPlans_WeeklyPlanId",
                table: "DayPlans",
                column: "WeeklyPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_DaySessions_DayPlanId",
                table: "DaySessions",
                column: "DayPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_DaySessionStores_DaySessionId",
                table: "DaySessionStores",
                column: "DaySessionId");

            migrationBuilder.CreateIndex(
                name: "IX_DaySessionStores_StoreId",
                table: "DaySessionStores",
                column: "StoreId");
        }
    }
}
