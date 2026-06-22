using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Auth.Migrations
{
    /// <inheritdoc />
    public partial class SeedRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Roles",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
            { new Guid("11111111-1111-1111-1111-111111111111"), "StationManager" },
            { new Guid("22222222-2222-2222-2222-222222222222"), "Staff" }
                });
        }
        

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
