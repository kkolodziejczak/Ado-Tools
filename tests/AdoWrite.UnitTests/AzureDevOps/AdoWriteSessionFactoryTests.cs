using System.Net;
using AdoWrite.AzureDevOps;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace AdoWrite.UnitTests.AzureDevOps;

public sealed class AdoWriteSessionFactoryTests
{
    /// <summary>
    /// The SDK resends a request after 502, 503 or 504 by default, a POST too. A comment Azure DevOps
    /// saved before it answered "unavailable" would then be posted again, past the marker check.
    /// </summary>
    [Fact]
    public async Task TheConnectionSendsEachRequestOnceEvenWhenAzureDevOpsIsUnavailable()
    {
        using var listener = new HttpListener();
        int port = Random.Shared.Next(20000, 40000);
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        int requests = 0;
        Task serving = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                try
                {
                    HttpListenerContext context = await listener.GetContextAsync();
                    Interlocked.Increment(ref requests);
                    context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                    context.Response.Close();
                }
                catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or InvalidOperationException)
                {
                    return;
                }
            }
        });

        // The SDK sends a token only over https, so this plain listener is asked without one.
        using VssConnection connection = AdoWriteSessionFactory.Connect(new Uri($"http://localhost:{port}/fabricated/"), new VssCredentials());
        await Assert.ThrowsAnyAsync<Exception>(() => connection.ConnectAsync());

        listener.Stop();
        await serving;
        Assert.Equal(1, requests);
    }
}
