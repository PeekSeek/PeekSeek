using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

public class DiscordWebhookExample
{
    private static readonly HttpClient client = new HttpClient();

    public static async Task SendMessage(string message)
    {
        string webhookUrl = "https://discord.com/api/webhooks/1494362623884525749/uJ_L2tcvmPi7jgC9HU7yu_Rl8Xg47OZF2odd7dA39ANw6X-gFZ7E6nGu81VOHkVQx7Jo";

        string json = $"{{\"content\":\"{message}\"}}";

        var content = new StringContent(json, Encoding.UTF8, "application/json");

        HttpResponseMessage response = await client.PostAsync(webhookUrl, content);

        string result = await response.Content.ReadAsStringAsync();

        System.Console.WriteLine(result);
    }
}