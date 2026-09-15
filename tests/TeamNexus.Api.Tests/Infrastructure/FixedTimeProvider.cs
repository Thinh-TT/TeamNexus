namespace TeamNexus.Api.Tests.Infrastructure;

/// <summary>
/// A <see cref="TimeProvider"/> frozen at one instant, for the Phase 12 dashboard suite.
/// <para>
/// The dashboard's three task buckets are defined by <b>boundaries</b> ("due exactly now is not
/// overdue", "due within N days", "assigned within 7 days"). Asserting those against the wall clock
/// would be flaky by construction — the request happens some milliseconds after the test computes its
/// own <c>UtcNow</c>, so a task seeded "exactly now" can land on either side. Injecting a fixed clock
/// moves the boundary from "somewhere in this millisecond" to an exact, reproducible value, which is
/// the same reason <c>ReportAggregator</c> takes <c>snapshot.Now</c> instead of calling
/// <c>DateTime.Now</c>.
/// </para>
/// <para>
/// Only <see cref="GetUtcNow"/> is overridden. Nothing in the product calls the timer APIs on the
/// dashboard path, and overriding them would require keeping a timer list alive for no benefit.
/// </para>
/// </summary>
public sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;

    public FixedTimeProvider(DateTimeOffset now) => _now = now;

    public override DateTimeOffset GetUtcNow() => _now;
}
