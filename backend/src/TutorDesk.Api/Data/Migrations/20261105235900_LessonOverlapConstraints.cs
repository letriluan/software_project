using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TutorDesk.Api.Data.Migrations;

/// <summary>
/// Database-level guarantee that a tutor (or a student) can never have two overlapping
/// non-cancelled lessons, even if two booking requests race past the application check.
/// Violations surface as SQLSTATE 23P01 and are returned to the client as 409 Conflict.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261105235900_LessonOverlapConstraints")]
public partial class LessonOverlapConstraints : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");
        migrationBuilder.Sql("""
            ALTER TABLE lessons ADD CONSTRAINT ex_lessons_tutor_no_overlap
            EXCLUDE USING gist ("TutorId" WITH =, tstzrange("StartsAtUtc", "EndsAtUtc", '[)') WITH &&)
            WHERE ("Status" <> 'Cancelled');
            """);
        migrationBuilder.Sql("""
            ALTER TABLE lessons ADD CONSTRAINT ex_lessons_student_no_overlap
            EXCLUDE USING gist ("StudentId" WITH =, tstzrange("StartsAtUtc", "EndsAtUtc", '[)') WITH &&)
            WHERE ("Status" <> 'Cancelled');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE lessons DROP CONSTRAINT IF EXISTS ex_lessons_student_no_overlap;");
        migrationBuilder.Sql("ALTER TABLE lessons DROP CONSTRAINT IF EXISTS ex_lessons_tutor_no_overlap;");
    }
}
