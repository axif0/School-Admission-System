using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolAdmission.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHoldClassForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM holds h WHERE NOT EXISTS (SELECT 1 FROM classes c WHERE c.id = h.class_id)) THEN
                        RAISE EXCEPTION 'Cannot add foreign key FK_holds_classes_class_id: orphan holds exist.';
                    END IF;
                END $$;");

            migrationBuilder.Sql(@"
                ALTER TABLE holds ADD CONSTRAINT ""FK_holds_classes_class_id""
                    FOREIGN KEY (class_id) REFERENCES classes(id) ON DELETE RESTRICT NOT VALID;
                ALTER TABLE holds VALIDATE CONSTRAINT ""FK_holds_classes_class_id"";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_holds_classes_class_id",
                table: "holds");
        }
    }
}
