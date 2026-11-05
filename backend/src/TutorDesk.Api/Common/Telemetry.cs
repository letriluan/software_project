using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace TutorDesk.Api.Common;

/// <summary>Custom business telemetry, exported to Application Insights via OpenTelemetry.</summary>
public static class Telemetry
{
    public const string Name = "TutorDesk";

    public static readonly ActivitySource Source = new(Name);
    private static readonly Meter Meter = new(Name);

    public static readonly Counter<long> LessonsBooked =
        Meter.CreateCounter<long>("tutordesk.lessons.booked", description: "Lessons created");

    public static readonly Counter<long> LessonConflicts =
        Meter.CreateCounter<long>("tutordesk.lessons.conflicts", description: "Bookings rejected for overlap");

    public static readonly Counter<long> AttendanceMarked =
        Meter.CreateCounter<long>("tutordesk.lessons.attendance", description: "Attendance outcomes recorded");
}
