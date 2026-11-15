using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace QuickCommands.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "categories",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false),
                    slug = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    name_de = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    name_en = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    sort_order = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_categories", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    firebase_uid = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    handle = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    last_seen_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    is_banned = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    banned_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ban_reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "cloud_save_revisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    user_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    schema_version = table.Column<int>(type: "int", nullable: false),
                    size_bytes = table.Column<int>(type: "int", nullable: false),
                    content = table.Column<string>(type: "mediumtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cloud_save_revisions", x => x.id);
                    table.ForeignKey(
                        name: "FK_cloud_save_revisions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "store_packages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    owner_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    title = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    category_id = table.Column<int>(type: "int", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    removal_reason = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    star_count = table.Column<int>(type: "int", nullable: false),
                    install_count = table.Column<int>(type: "int", nullable: false),
                    open_report_count = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    removed_at = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_store_packages", x => x.id);
                    table.ForeignKey(
                        name: "FK_store_packages_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_store_packages_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "store_commands",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    package_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    name = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    language = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    script = table.Column<string>(type: "mediumtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    parameters_json = table.Column<string>(type: "mediumtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_store_commands", x => x.id);
                    table.ForeignKey(
                        name: "FK_store_commands_store_packages_package_id",
                        column: x => x.package_id,
                        principalTable: "store_packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "store_installs",
                columns: table => new
                {
                    package_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    user_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_store_installs", x => new { x.package_id, x.user_id });
                    table.ForeignKey(
                        name: "FK_store_installs_store_packages_package_id",
                        column: x => x.package_id,
                        principalTable: "store_packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_store_installs_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "store_package_tags",
                columns: table => new
                {
                    package_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tag = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_store_package_tags", x => new { x.package_id, x.tag });
                    table.ForeignKey(
                        name: "FK_store_package_tags_store_packages_package_id",
                        column: x => x.package_id,
                        principalTable: "store_packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "store_reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    package_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    reporter_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    reason = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    comment = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    resolved_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    resolution = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_store_reports", x => x.id);
                    table.ForeignKey(
                        name: "FK_store_reports_store_packages_package_id",
                        column: x => x.package_id,
                        principalTable: "store_packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_store_reports_users_reporter_id",
                        column: x => x.reporter_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "store_stars",
                columns: table => new
                {
                    package_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    user_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_store_stars", x => new { x.package_id, x.user_id });
                    table.ForeignKey(
                        name: "FK_store_stars_store_packages_package_id",
                        column: x => x.package_id,
                        principalTable: "store_packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_store_stars_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "categories",
                columns: new[] { "id", "name_de", "name_en", "slug", "sort_order" },
                values: new object[,]
                {
                    { 1, "Git", "Git", "git", 10 },
                    { 2, "Navigation", "Navigation", "navigation", 20 },
                    { 3, "Dateien", "Files", "files", 30 },
                    { 4, "Docker", "Docker", "docker", 40 },
                    { 5, "Build", "Build", "build", 50 },
                    { 6, "Entwicklung", "Dev tools", "dev-tools", 60 },
                    { 7, "Netzwerk", "Network", "network", 70 },
                    { 8, "System", "System", "system", 80 },
                    { 9, "Sonstiges", "Other", "other", 1000 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_categories_slug",
                table: "categories",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cloud_save_revisions_user_id_revision",
                table: "cloud_save_revisions",
                columns: new[] { "user_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_store_commands_name",
                table: "store_commands",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "IX_store_commands_package_id_sort_order",
                table: "store_commands",
                columns: new[] { "package_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "IX_store_installs_user_id",
                table: "store_installs",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_store_package_tags_tag",
                table: "store_package_tags",
                column: "tag");

            migrationBuilder.CreateIndex(
                name: "IX_store_packages_category_id",
                table: "store_packages",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "IX_store_packages_owner_id",
                table: "store_packages",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "IX_store_packages_status_created_at",
                table: "store_packages",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_store_packages_status_install_count",
                table: "store_packages",
                columns: new[] { "status", "install_count" });

            migrationBuilder.CreateIndex(
                name: "IX_store_packages_status_star_count",
                table: "store_packages",
                columns: new[] { "status", "star_count" });

            migrationBuilder.CreateIndex(
                name: "IX_store_reports_package_id_reporter_id",
                table: "store_reports",
                columns: new[] { "package_id", "reporter_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_store_reports_reporter_id",
                table: "store_reports",
                column: "reporter_id");

            migrationBuilder.CreateIndex(
                name: "IX_store_reports_resolved_at_created_at",
                table: "store_reports",
                columns: new[] { "resolved_at", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_store_stars_user_id",
                table: "store_stars",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_users_firebase_uid",
                table: "users",
                column: "firebase_uid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_handle",
                table: "users",
                column: "handle",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cloud_save_revisions");

            migrationBuilder.DropTable(
                name: "store_commands");

            migrationBuilder.DropTable(
                name: "store_installs");

            migrationBuilder.DropTable(
                name: "store_package_tags");

            migrationBuilder.DropTable(
                name: "store_reports");

            migrationBuilder.DropTable(
                name: "store_stars");

            migrationBuilder.DropTable(
                name: "store_packages");

            migrationBuilder.DropTable(
                name: "categories");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
