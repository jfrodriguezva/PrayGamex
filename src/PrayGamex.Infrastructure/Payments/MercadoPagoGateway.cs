using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PrayGamex.Application.Abstractions;
using PrayGamex.Domain.Entities;
namespace PrayGamex.Infrastructure.Payments;

public class MercadoPagoOptions
{
    public string AccessToken { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.mercadopago.com";
    public string NotificationUrl { get; set; } = "";
    public string BackUrl { get; set; } = "";
    /// <summary>Clave secreta de webhooks (panel de Mercado Pago → Webhooks). Obligatoria fuera de Development.</summary>
    public string WebhookSecret { get; set; } = "";
}

/// <summary>Cliente de Mercado Pago. La resiliencia (reintentos, circuit breaker, timeout – Polly) se configura en el HttpClient.</summary>
public class MercadoPagoGateway(HttpClient http, IOptions<MercadoPagoOptions> opt) : IPaymentGateway
{
    private readonly MercadoPagoOptions o = opt.Value;

    public async Task<(string PreferenceId, string CheckoutUrl)> CreatePreferenceAsync(Payment payment, string payerEmail, CancellationToken ct)
    {
        var body = new
        {
            items = new[] { new { title = payment.Title, quantity = 1, unit_price = payment.Amount, currency_id = payment.Currency } },
            payer = new { email = payerEmail },
            external_reference = payment.Id.ToString(),
            notification_url = o.NotificationUrl,
            back_urls = new { success = o.BackUrl, failure = o.BackUrl, pending = o.BackUrl },
            auto_return = "approved",
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, "/checkout/preferences") { Content = JsonContent.Create(body) };
        // Idempotencia: un reintento de Polly no duplica la preferencia.
        req.Headers.Add("X-Idempotency-Key", payment.Id.ToString());
        using var res = await http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadFromJsonAsync<JsonElement>(ct);
        return (json.GetProperty("id").GetString()!, json.GetProperty("init_point").GetString()!);
    }

    public async Task<(string Status, string? ExternalReference, decimal Amount)> GetPaymentAsync(string externalPaymentId, CancellationToken ct)
    {
        var json = await http.GetFromJsonAsync<JsonElement>($"/v1/payments/{Uri.EscapeDataString(externalPaymentId)}", ct);
        return (json.GetProperty("status").GetString() ?? "pending",
            json.TryGetProperty("external_reference", out var r) ? r.GetString() : null,
            json.TryGetProperty("transaction_amount", out var a) ? a.GetDecimal() : 0m);
    }
}

/// <summary>Verifica la cabecera x-signature de los webhooks de Mercado Pago (HMAC-SHA256 del manifiesto "id:..;request-id:..;ts:..;").</summary>
public static class MercadoPagoSignature
{
    public static bool Verify(string secret, string? xSignature, string? xRequestId, string? dataId)
    {
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(xSignature) || string.IsNullOrEmpty(dataId)) return false;
        string? ts = null, v1 = null;
        foreach (var part in xSignature.Split(','))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2) continue;
            if (kv[0].Trim() == "ts") ts = kv[1].Trim(); else if (kv[0].Trim() == "v1") v1 = kv[1].Trim();
        }
        if (ts is null || v1 is null) return false;
        var id = dataId.All(char.IsLetterOrDigit) ? dataId.ToLowerInvariant() : dataId;
        var manifest = $"id:{id};request-id:{xRequestId};ts:{ts};";
        var expected = Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(secret), System.Text.Encoding.UTF8.GetBytes(manifest))).ToLowerInvariant();
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(expected), System.Text.Encoding.ASCII.GetBytes(v1.ToLowerInvariant()));
    }
}