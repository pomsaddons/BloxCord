using BloxCord.Api.Hubs;
using BloxCord.Api.Models;
using BloxCord.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://localhost:5158");

builder.Services.AddCors(options =>
{
	options.AddDefaultPolicy(policy =>
		policy.AllowAnyHeader()
			  .AllowAnyMethod()
			  .AllowAnyOrigin());
});

builder.Services.AddSignalR();
builder.Services.AddSingleton<ChannelRegistry>();
builder.Services.AddSingleton<RobloxAvatarService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();

app.MapPost("/api/channels", async (CreateChannelRequest request, ChannelRegistry registry, RobloxAvatarService avatarService, CancellationToken ct) =>
{
	string? avatarUrl = null;
	if (request.UserId.HasValue)
		avatarUrl = await avatarService.TryGetHeadshotUrlAsync(request.UserId.Value, ct);

	var channel = registry.CreateOrGetChannel(request.JobId, request.Username, request.UserId, avatarUrl);

	return Results.Ok(new ChannelResponse
	{
		JobId = channel.JobId,
		CreatedAt = channel.CreatedAt,
		CreatedBy = channel.CreatedBy,
		Participants = channel.GetParticipants()
	});
});

app.MapGet("/api/channels/{jobId}", (string jobId, ChannelRegistry registry) =>
{
	if (!registry.TryGetChannel(jobId, out var channel))
		return Results.NotFound();

	return Results.Ok(new ChannelSnapshot
	{
		JobId = channel!.JobId,
		CreatedAt = channel.CreatedAt,
		CreatedBy = channel.CreatedBy,
		Participants = channel.GetParticipants(),
		History = channel.GetHistory()
	});
});

app.MapHub<ChannelHub>("/hubs/chat");

app.Run();
