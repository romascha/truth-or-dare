namespace TruthOrDare.Api.Models;

public enum PromptType { Truth = 1, Dare = 2 }
public enum TurnStatus { Open = 1, Answered = 2, Skipped = 3, TimedOut = 4 }
public enum GameStatus { Active = 1, Ended = 2 }
public enum Gender { Other = 0, Male = 1, Female = 2 }

public sealed class User
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public Gender Gender { get; set; }
    public string SessionToken { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Game
{
    public Guid Id { get; set; }
    public string PublicCode { get; set; } = string.Empty;
    public string InviteTokenHash { get; set; } = string.Empty;
    public bool IsInviteActive { get; set; } = true;
    public int MaxPlayers { get; set; } = 2;
    public string Category { get; set; } = "Light";
    public GameStatus Status { get; set; } = GameStatus.Active;
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? InviteExpiresAt { get; set; }
    public List<GamePlayer> Players { get; set; } = [];
    public List<Turn> Turns { get; set; } = [];
}

public sealed class GamePlayer
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public Guid UserId { get; set; }
    public int SeatNumber { get; set; }
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
    public Game? Game { get; set; }
    public User? User { get; set; }
}

public sealed class Turn
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public Guid PlayerUserId { get; set; }
    public PromptType PromptType { get; set; }
    public string Category { get; set; } = "Light";
    public string PromptText { get; set; } = string.Empty;
    public string? AnswerText { get; set; }
    public TurnStatus Status { get; set; } = TurnStatus.Open;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? AnsweredAt { get; set; }
    public Game? Game { get; set; }
}

public sealed record PromptItem(string Type, string Category, string Text);
