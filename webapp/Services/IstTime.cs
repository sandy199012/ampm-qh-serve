namespace AMPMWeb.Services;

// Render's container runs on UTC, not Indian time — so every DateTime.Now /
// DateTime.Today call in this app was previously stamping tickets, to-dos,
// checklist entries, PO/bill dates, and Excel report headers with UTC time,
// which reads as ~5.5 hours behind what Sandy actually sees on his clock.
// India Standard Time never observes daylight saving, so its UTC offset is
// always exactly +5:30 — no TimeZoneInfo/tzdata lookup needed (which can be
// missing entirely in the slim Linux containers Render deploys; relying on
// it would risk a crash instead of just a wrong time). Use IstTime.Now /
// IstTime.Today everywhere in place of DateTime.Now / DateTime.Today so
// every timestamp in the app reflects real Indian time, regardless of what
// timezone the underlying server/container itself is set to.
public static class IstTime
{
    static readonly TimeSpan Offset = TimeSpan.FromMinutes(5 * 60 + 30);
    public static DateTime Now => DateTime.UtcNow + Offset;
    public static DateTime Today => Now.Date;
}
