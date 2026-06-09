using System.Security.Cryptography;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using TruthOrDare.Api.Data;
using TruthOrDare.Api.Hubs;
using TruthOrDare.Api.Models;
using TruthOrDare.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddSingleton<PromptService>();
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseSwagger();
app.UseSwaggerUI();

app.MapHub<GameHub>("/hubs/game");

// --- Auth helper ---
static async Task<User?> GetAuthenticatedUser(HttpContext http, AppDbContext db)
{
    if (!http.Request.Headers.TryGetValue("Authorization", out var value)) return null;
    var token = value.ToString().Replace("Bearer ", "");
    if (string.IsNullOrWhiteSpace(token)) return null;
    return await db.Users.FirstOrDefaultAsync(x => x.SessionToken == token);
}

// --- Timeout helper ---
static async Task ExpireTimedOutTurns(AppDbContext db, Guid gameId)
{
    var cutoff = DateTimeOffset.UtcNow.AddMinutes(-10);
    var openTurns = await db.Turns
        .Where(x => x.GameId == gameId && x.Status == TurnStatus.Open)
        .ToListAsync();
    var expired = openTurns.Where(x => x.CreatedAt < cutoff).ToList();
    foreach (var t in expired)
    {
        t.Status = TurnStatus.TimedOut;
        t.AnsweredAt = DateTimeOffset.UtcNow;
    }
    if (expired.Count > 0) await db.SaveChangesAsync();
}

// --- Login ---
app.MapPost("/api/users/prototype-login", async (PrototypeLoginRequest request, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(request.DisplayName))
        return Results.BadRequest("Display name is required.");

    var normalized = request.DisplayName.Trim();
    if (normalized.Length > 30) return Results.BadRequest("Name too long (max 30).");

    var user = await db.Users.FirstOrDefaultAsync(x => x.DisplayName == normalized);
    if (user is null)
    {
        user = new User
        {
            Id = Guid.NewGuid(),
            DisplayName = normalized,
            Gender = Enum.TryParse<Gender>(request.Gender, true, out var g) ? g : Gender.Other,
            SessionToken = GenerateSessionToken()
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
    }
    else
    {
        if (Enum.TryParse<Gender>(request.Gender, true, out var g))
            user.Gender = g;
        user.SessionToken = GenerateSessionToken();
        await db.SaveChangesAsync();
    }

    return Results.Ok(new { user.Id, user.DisplayName, Gender = user.Gender.ToString(), Token = user.SessionToken });
});

// --- Create Game ---
app.MapPost("/api/games", async (CreateGameRequest request, AppDbContext db, HttpContext http) =>
{
    var user = await GetAuthenticatedUser(http, db);
    if (user is null) return Results.Unauthorized();

    string code;
    do { code = InviteService.CreatePublicCode(); }
    while (await db.Games.AnyAsync(x => x.PublicCode == code));

    var token = InviteService.CreateToken();
    var game = new Game
    {
        Id = Guid.NewGuid(),
        PublicCode = code,
        InviteTokenHash = InviteService.HashToken(token),
        CreatedByUserId = user.Id,
        MaxPlayers = request.MaxPlayers <= 0 ? 2 : Math.Min(request.MaxPlayers, 10),
        Category = string.IsNullOrWhiteSpace(request.Category) ? "Light" : request.Category.Trim(),
        InviteExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
    };

    db.Games.Add(game);
    db.GamePlayers.Add(new GamePlayer
    {
        Id = Guid.NewGuid(),
        GameId = game.Id,
        UserId = user.Id,
        SeatNumber = 1
    });

    await db.SaveChangesAsync();

    var inviteUrl = $"{http.Request.Scheme}://{http.Request.Host}/join.html?code={code}&token={Uri.EscapeDataString(token)}";
    return Results.Ok(new { game.Id, game.PublicCode, InviteUrl = inviteUrl });
});

// --- Join Game ---
app.MapPost("/api/games/join", async (JoinGameRequest request, AppDbContext db, HttpContext http, IHubContext<GameHub> hub) =>
{
    var user = await GetAuthenticatedUser(http, db);
    if (user is null) return Results.Unauthorized();

    var game = await db.Games.Include(x => x.Players).FirstOrDefaultAsync(x => x.PublicCode == request.PublicCode);
    if (game is null) return Results.NotFound("Game not found.");
    if (game.Status == GameStatus.Ended) return Results.BadRequest("Game has ended.");
    if (!game.IsInviteActive) return Results.BadRequest("Invite is no longer active.");
    if (game.InviteExpiresAt < DateTimeOffset.UtcNow) return Results.BadRequest("Invite expired.");
    if (game.InviteTokenHash != InviteService.HashToken(request.Token)) return Results.Forbid();

    if (game.Players.Any(x => x.UserId == user.Id))
        return Results.Ok(new { game.Id, game.PublicCode, AlreadyJoined = true });

    if (game.Players.Count >= game.MaxPlayers)
        return Results.BadRequest("Game is full.");

    db.GamePlayers.Add(new GamePlayer
    {
        Id = Guid.NewGuid(),
        GameId = game.Id,
        UserId = user.Id,
        SeatNumber = game.Players.Count + 1
    });

    if (game.Players.Count + 1 >= game.MaxPlayers) game.IsInviteActive = false;

    await db.SaveChangesAsync();

    await hub.Clients.Group(game.Id.ToString()).SendAsync("PlayerJoined", new { user.Id, user.DisplayName, Gender = user.Gender.ToString() });

    return Results.Ok(new { game.Id, game.PublicCode, AlreadyJoined = false });
});

// --- Get Game ---
app.MapGet("/api/games/{gameId:guid}", async (Guid gameId, AppDbContext db, HttpContext http, PromptService prompts) =>
{
    var user = await GetAuthenticatedUser(http, db);
    if (user is null) return Results.Unauthorized();
    if (!await IsPlayer(db, gameId, user.Id)) return Results.Forbid();

    await ExpireTimedOutTurns(db, gameId);

    var game = await db.Games
        .Include(x => x.Players).ThenInclude(x => x.User)
        .Include(x => x.Turns)
        .FirstAsync(x => x.Id == gameId);

    // Determine whose turn it is
    var players = game.Players.OrderBy(x => x.SeatNumber).ToList();
    var allTurns = game.Turns.OrderBy(x => x.CreatedAt).ToList();
    var lastTurn = allTurns.LastOrDefault();
    Guid? currentPlayerId = null;
    if (players.Count >= 2 && !game.Turns.Any(x => x.Status == TurnStatus.Open))
    {
        if (lastTurn is null)
            currentPlayerId = players.First().UserId;
        else
        {
            var idx = players.FindIndex(x => x.UserId == lastTurn.PlayerUserId);
            currentPlayerId = players[(idx + 1) % players.Count].UserId;
        }
    }

    var turns = allTurns.OrderByDescending(x => x.CreatedAt).Select(x => new
    {
        x.Id,
        x.PlayerUserId,
        PlayerName = game.Players.FirstOrDefault(p => p.UserId == x.PlayerUserId)?.User?.DisplayName,
        PromptType = x.PromptType.ToString(),
        x.Category,
        x.PromptText,
        x.AnswerText,
        Status = x.Status.ToString(),
        x.CreatedAt,
        x.AnsweredAt
    }).ToList();

    var usedTexts = game.Turns.Select(x => x.PromptText).ToHashSet();
    var promptsExhausted = prompts.CountAvailable(game.Category, usedTexts) == 0;

    return Results.Ok(new
    {
        game.Id,
        game.PublicCode,
        game.Category,
        Status = game.Status.ToString(),
        CurrentPlayerId = currentPlayerId,
        PromptsExhausted = promptsExhausted,
        Players = players.Select(x => new { x.UserId, x.User!.DisplayName, Gender = x.User.Gender.ToString(), x.SeatNumber }),
        Turns = turns
    });
});

// --- Choose (active player picks Truth/Dare/Random and gets a prompt) ---
app.MapPost("/api/games/{gameId:guid}/choose", async (Guid gameId, ChooseRequest request, AppDbContext db, PromptService prompts, HttpContext http, IHubContext<GameHub> hub) =>
{
    var user = await GetAuthenticatedUser(http, db);
    if (user is null) return Results.Unauthorized();
    if (!await IsPlayer(db, gameId, user.Id)) return Results.Forbid();

    var game = await db.Games.Include(x => x.Players).Include(x => x.Turns).FirstAsync(x => x.Id == gameId);
    if (game.Status == GameStatus.Ended) return Results.BadRequest("Game has ended.");

    await ExpireTimedOutTurns(db, gameId);

    if (game.Turns.Any(x => x.Status == TurnStatus.Open))
        return Results.BadRequest("There is already an open turn.");

    var players = game.Players.OrderBy(x => x.SeatNumber).ToList();
    if (players.Count < 2) return Results.BadRequest("Wait for the second player.");

    // Determine whose turn it is
    var allTurns = game.Turns.OrderBy(x => x.CreatedAt).ToList();
    var lastTurn = allTurns.LastOrDefault();
    GamePlayer nextPlayer;
    if (lastTurn is null)
        nextPlayer = players.First();
    else
    {
        var idx = players.FindIndex(x => x.UserId == lastTurn.PlayerUserId);
        nextPlayer = players[(idx + 1) % players.Count];
    }

    if (nextPlayer.UserId != user.Id)
        return Results.BadRequest("It's not your turn.");

    // Resolve type
    PromptType type;
    if (request.Choice.Equals("Random", StringComparison.OrdinalIgnoreCase))
        type = Random.Shared.Next(2) == 0 ? PromptType.Truth : PromptType.Dare;
    else if (request.Choice.Equals("Dare", StringComparison.OrdinalIgnoreCase))
        type = PromptType.Dare;
    else
        type = PromptType.Truth;

    var usedTexts = game.Turns.Select(x => x.PromptText).ToHashSet();
    var prompt = prompts.GetRandomExcluding(type, game.Category, usedTexts);
    if (prompt is null)
        return Results.Ok(new { PromptsExhausted = true });

    var turn = new Turn
    {
        Id = Guid.NewGuid(),
        GameId = gameId,
        PlayerUserId = user.Id,
        PromptType = type,
        Category = prompt.Category,
        PromptText = prompt.Text
    };

    db.Turns.Add(turn);
    await db.SaveChangesAsync();

    var payload = new
    {
        turn.Id,
        turn.PlayerUserId,
        PlayerName = user.DisplayName,
        PromptType = turn.PromptType.ToString(),
        turn.Category,
        turn.PromptText,
        Status = turn.Status.ToString(),
        turn.CreatedAt
    };

    await hub.Clients.Group(gameId.ToString()).SendAsync("TurnCreated", payload);

    return Results.Ok(payload);
});

// --- Answer Turn (for Truth) ---
app.MapPost("/api/turns/{turnId:guid}/answer", async (Guid turnId, AnswerTurnRequest request, AppDbContext db, HttpContext http, IHubContext<GameHub> hub) =>
{
    var user = await GetAuthenticatedUser(http, db);
    if (user is null) return Results.Unauthorized();

    var turn = await db.Turns.FirstOrDefaultAsync(x => x.Id == turnId);
    if (turn is null) return Results.NotFound();
    if (turn.PlayerUserId != user.Id) return Results.Forbid();
    if (turn.Status != TurnStatus.Open) return Results.BadRequest("Turn is not open.");

    if (turn.CreatedAt < DateTimeOffset.UtcNow.AddMinutes(-10))
    {
        turn.Status = TurnStatus.TimedOut;
        turn.AnsweredAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return Results.BadRequest("Turn has timed out.");
    }

    turn.AnswerText = request.AnswerText?.Trim();
    turn.Status = TurnStatus.Answered;
    turn.AnsweredAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();

    await hub.Clients.Group(turn.GameId.ToString()).SendAsync("TurnAnswered", new
    {
        turn.Id,
        turn.PlayerUserId,
        turn.AnswerText,
        Status = turn.Status.ToString()
    });

    return Results.Ok(new { turn.Id, Status = "Answered" });
});

// --- Done (for Dare) ---
app.MapPost("/api/turns/{turnId:guid}/done", async (Guid turnId, DoneTurnRequest request, AppDbContext db, HttpContext http, IHubContext<GameHub> hub) =>
{
    var user = await GetAuthenticatedUser(http, db);
    if (user is null) return Results.Unauthorized();

    var turn = await db.Turns.FirstOrDefaultAsync(x => x.Id == turnId);
    if (turn is null) return Results.NotFound();
    if (turn.PlayerUserId != user.Id) return Results.Forbid();
    if (turn.Status != TurnStatus.Open) return Results.BadRequest("Turn is not open.");

    if (turn.CreatedAt < DateTimeOffset.UtcNow.AddMinutes(-10))
    {
        turn.Status = TurnStatus.TimedOut;
        turn.AnsweredAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return Results.BadRequest("Turn has timed out.");
    }

    turn.AnswerText = string.IsNullOrWhiteSpace(request.Comment) ? "✓ Выполнено" : $"✓ Выполнено: {request.Comment.Trim()}";
    turn.Status = TurnStatus.Answered;
    turn.AnsweredAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();

    await hub.Clients.Group(turn.GameId.ToString()).SendAsync("TurnAnswered", new
    {
        turn.Id,
        turn.PlayerUserId,
        turn.AnswerText,
        Status = turn.Status.ToString()
    });

    return Results.Ok(new { turn.Id, Status = "Answered" });
});

// --- Skip Turn ---
app.MapPost("/api/turns/{turnId:guid}/skip", async (Guid turnId, AppDbContext db, HttpContext http, IHubContext<GameHub> hub) =>
{
    var user = await GetAuthenticatedUser(http, db);
    if (user is null) return Results.Unauthorized();

    var turn = await db.Turns.FirstOrDefaultAsync(x => x.Id == turnId);
    if (turn is null) return Results.NotFound();
    if (turn.PlayerUserId != user.Id) return Results.Forbid();
    if (turn.Status != TurnStatus.Open) return Results.BadRequest("Turn is not open.");

    turn.Status = TurnStatus.Skipped;
    turn.AnsweredAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();

    await hub.Clients.Group(turn.GameId.ToString()).SendAsync("TurnSkipped", new
    {
        turn.Id,
        turn.PlayerUserId,
        Status = turn.Status.ToString()
    });

    return Results.Ok(new { turn.Id, Status = "Skipped" });
});

// --- End Game ---
app.MapPost("/api/games/{gameId:guid}/end", async (Guid gameId, AppDbContext db, HttpContext http, IHubContext<GameHub> hub) =>
{
    var user = await GetAuthenticatedUser(http, db);
    if (user is null) return Results.Unauthorized();
    if (!await IsPlayer(db, gameId, user.Id)) return Results.Forbid();

    var game = await db.Games.FirstAsync(x => x.Id == gameId);
    if (game.Status == GameStatus.Ended) return Results.BadRequest("Game already ended.");

    game.Status = GameStatus.Ended;
    game.IsInviteActive = false;

    var openTurns = await db.Turns.Where(x => x.GameId == gameId && x.Status == TurnStatus.Open).ToListAsync();
    foreach (var t in openTurns)
    {
        t.Status = TurnStatus.TimedOut;
        t.AnsweredAt = DateTimeOffset.UtcNow;
    }

    await db.SaveChangesAsync();

    await hub.Clients.Group(gameId.ToString()).SendAsync("GameEnded", new { gameId });

    return Results.Ok(new { game.Id, Status = "Ended" });
});

app.Run();

static string GenerateSessionToken()
{
    var bytes = RandomNumberGenerator.GetBytes(32);
    return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").Replace("=", "");
}

static async Task<bool> IsPlayer(AppDbContext db, Guid gameId, Guid userId)
    => await db.GamePlayers.AnyAsync(x => x.GameId == gameId && x.UserId == userId);

public sealed record PrototypeLoginRequest(string DisplayName, string? Gender);
public sealed record CreateGameRequest(int MaxPlayers = 2, string? Category = "Light");
public sealed record JoinGameRequest(string PublicCode, string Token);
public sealed record ChooseRequest(string Choice);
public sealed record AnswerTurnRequest(string? AnswerText);
public sealed record DoneTurnRequest(string? Comment);
