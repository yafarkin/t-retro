using System.Net;
using System.Net.Http.Json;
using Api.Contracts;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace Api.Tests;

public static class SignalRTestHelpers
{
    public static async Task<HubConnection> ConnectAsync(
        TRetroWebApplicationFactory factory,
        HttpClient client,
        string subject,
        Guid meetingId)
    {
        var response = await client.PostAsJsonAsync("/api/dev/login", new DevLoginRequest(subject, subject));
        response.EnsureSuccessStatusCode();
        var session = response.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        var cookies = new CookieContainer();
        cookies.Add(new Uri("http://localhost"), new Cookie(
            session.Split('=', 2)[0],
            session.Split('=', 2)[1]));
        var connection = new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/retro", options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Cookies = cookies;
                options.Headers.Add("Cookie", session);
            })
            .Build();
        await connection.StartAsync();
        await connection.InvokeAsync("Subscribe", meetingId);
        return connection;
    }

    public static async Task<MeetingStateResponse> WaitForStateAsync(
        HttpClient client,
        Guid meetingId,
        Func<MeetingStateResponse, bool> predicate)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var state = await client.GetFromJsonAsync<MeetingStateResponse>($"/api/meetings/{meetingId}/state");
            if (state is not null && predicate(state))
            {
                return state;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Expected meeting state was not observed.");
    }
}
