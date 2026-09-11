using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Persistence.Data.Configurations;

/// <summary>
/// Maps <see cref="MemberType"/> to/from the lowercase text values required by the DB design §4
/// (<c>'human' | 'ai_agent'</c>) and by the API contract.
/// <para>
/// <c>HasConversion&lt;string&gt;()</c> would store the CLR names (<c>"Human"</c>/<c>"AiAgent"</c>)
/// and therefore violate <c>ck_workspace_members_member_type</c> (a real insert failure), so the two
/// explicit conversions below are the smallest correct mapping. The generic
/// <see cref="ValueConverter{TModel,TProvider}"/> base is used to keep ModelClrType/ProviderClrType
/// explicit for the migration snapshot (Phase 7 §2.1).
/// </para>
/// </summary>
public sealed class MemberTypeToStringConverter : ValueConverter<MemberType, string>
{
    private static readonly Expression<Func<MemberType, string>> ToProvider =
        v => v == MemberType.AiAgent ? "ai_agent" : "human";

    private static readonly Expression<Func<string, MemberType>> FromProvider =
        v => v == "ai_agent" ? MemberType.AiAgent : MemberType.Human;

    public MemberTypeToStringConverter()
        : base(ToProvider, FromProvider)
    {
    }
}
