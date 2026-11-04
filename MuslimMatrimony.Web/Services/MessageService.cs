using Microsoft.EntityFrameworkCore;
using MuslimMatrimony.Data;
using MuslimMatrimony.Models;

namespace MuslimMatrimony.Services;

public sealed record ConversationSummary(string OtherUserId, string OtherFirstName, DateTime LastMessageAt, string LastMessagePreview, int Unread);

public sealed record ChatMessage(long Id, bool Mine, string Body, DateTime SentAt, DateTime? ReadAt);

public enum SendOutcome
{
    Sent,
    NotAllowed,
    Invalid,
}

/// <summary>Messaging is only possible between mutual matches. History stays readable unless one of them blocks the other.</summary>
public sealed class MessageService(ApplicationDbContext db, MatchService matches)
{
    public const int MaxLength = 2000;

    public async Task<SendOutcome> SendAsync(string senderId, string recipientId, string? body)
    {
        var text = body?.Trim() ?? string.Empty;
        if (text.Length is 0 or > MaxLength)
        {
            return SendOutcome.Invalid;
        }

        if (!await matches.IsMatchAsync(senderId, recipientId))
        {
            return SendOutcome.NotAllowed;
        }

        db.Messages.Add(new Message { SenderId = senderId, RecipientId = recipientId, Body = text, SentAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return SendOutcome.Sent;
    }

    private Task<bool> BlockedAsync(string a, string b) =>
        db.Blocks.AnyAsync(x => (x.BlockerId == a && x.BlockedId == b) || (x.BlockerId == b && x.BlockedId == a));

    public async Task<List<ConversationSummary>> ConversationsAsync(string userId)
    {
        var groups = await db.Messages
            .Where(m => m.SenderId == userId || m.RecipientId == userId)
            .Select(m => new { Other = m.SenderId == userId ? m.RecipientId : m.SenderId, m.SentAt, Unread = m.RecipientId == userId && m.ReadAt == null })
            .GroupBy(x => x.Other)
            .Select(g => new { Other = g.Key, Last = g.Max(x => x.SentAt), Unread = g.Count(x => x.Unread) })
            .OrderByDescending(g => g.Last)
            .Take(200)
            .ToListAsync();

        var ids = groups.Select(g => g.Other).ToList();
        var blocked = await db.Blocks.Where(b => (b.BlockerId == userId && ids.Contains(b.BlockedId)) || (b.BlockedId == userId && ids.Contains(b.BlockerId)))
            .Select(b => b.BlockerId == userId ? b.BlockedId : b.BlockerId).ToListAsync();
        var names = await db.Users.Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FirstName);
        var previews = new Dictionary<string, string>();
        foreach (var g in groups.Where(g => !blocked.Contains(g.Other)))
        {
            var last = await db.Messages.Where(m => (m.SenderId == userId && m.RecipientId == g.Other) || (m.SenderId == g.Other && m.RecipientId == userId))
                .OrderByDescending(m => m.SentAt).ThenByDescending(m => m.Id).Select(m => m.Body).FirstAsync();
            previews[g.Other] = last.Length > 80 ? string.Concat(last.AsSpan(0, 80), "…") : last;
        }

        return groups.Where(g => !blocked.Contains(g.Other) && names.ContainsKey(g.Other))
            .Select(g => new ConversationSummary(g.Other, names[g.Other], g.Last, previews[g.Other], g.Unread)).ToList();
    }

    /// <summary>The latest messages with <paramref name="otherId"/>, oldest first, marking received ones as read. Null if not allowed.</summary>
    public async Task<List<ChatMessage>?> ThreadAsync(string userId, string otherId, int take = 200)
    {
        if (userId == otherId || await BlockedAsync(userId, otherId))
        {
            return null;
        }

        var pair = db.Messages.Where(m => (m.SenderId == userId && m.RecipientId == otherId) || (m.SenderId == otherId && m.RecipientId == userId));
        if (!await pair.AnyAsync() && !await matches.IsMatchAsync(userId, otherId))
        {
            return null;
        }

        var now = DateTime.UtcNow;
        await db.Messages.Where(m => m.SenderId == otherId && m.RecipientId == userId && m.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ReadAt, now));

        var rows = await pair.OrderByDescending(m => m.SentAt).ThenByDescending(m => m.Id).Take(take)
            .Select(m => new ChatMessage(m.Id, m.SenderId == userId, m.Body, m.SentAt, m.ReadAt)).ToListAsync();
        rows.Reverse();
        return rows;
    }

    public Task<int> UnreadCountAsync(string userId) =>
        db.Messages.CountAsync(m => m.RecipientId == userId && m.ReadAt == null &&
                                   !db.Blocks.Any(b => (b.BlockerId == userId && b.BlockedId == m.SenderId) || (b.BlockerId == m.SenderId && b.BlockedId == userId)));
}
