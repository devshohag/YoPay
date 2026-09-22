using Microsoft.EntityFrameworkCore;
using YoPay.Application.Security;
using YoPay.Domain.Entities;
using YoPay.Domain.Enums;
using YoPay.Infrastructure.Persistence;

// Creates one merchant, one wallet and one API credential, so the API and the checkout
// page can actually be driven locally. Development only: it prints a secret to the
// console, which is the one thing a production tool must never do.
//
//   dotnet run --project tools/YoPay.Seed -- "Demo Shop" 01700000000

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
    ?? "Host=localhost;Port=5433;Database=yopay;Username=yopay;Password=yopay";

var activeKeyId = Environment.GetEnvironmentVariable("Security__ActiveKeyId");
if (string.IsNullOrWhiteSpace(activeKeyId))
{
    Console.Error.WriteLine(
        "Security__ActiveKeyId is not set. The HMAC secret has to be encrypted with the " +
        "same key ring the API will use to read it back, so seeding without one would " +
        "produce a credential nothing can decrypt.");
    return 1;
}

var keys = new Dictionary<string, string>(StringComparer.Ordinal);
foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
{
    var name = entry.Key.ToString() ?? "";
    if (name.StartsWith("Security__Keys__", StringComparison.Ordinal))
    {
        keys[name["Security__Keys__".Length..]] = entry.Value?.ToString() ?? "";
    }
}

var protector = new AesGcmSecretProtector(new SecretProtectionOptions
{
    ActiveKeyId = activeKeyId,
    Keys = keys,
});

// A pairing code for an existing wallet, so the simulator (and later the app) can join.
if (args.Length > 0 && args[0].Equals("pair-token", StringComparison.OrdinalIgnoreCase))
{
    var tokenOptions = new DbContextOptionsBuilder<YoPayDbContext>()
        .UseNpgsql(connectionString)
        .Options;

    await using var tokenDb = new YoPayDbContext(tokenOptions);

    var targetWallet = await tokenDb.Wallets.OrderBy(w => w.CreatedAt).FirstOrDefaultAsync();
    if (targetWallet is null)
    {
        Console.Error.WriteLine("No wallet exists yet. Seed a merchant first.");
        return 1;
    }

    var pairingCode = YoPay.Application.Devices.PairingToken.New();

    tokenDb.DevicePairingTokens.Add(new DevicePairingToken
    {
        MerchantId = targetWallet.MerchantId,
        WalletId = targetWallet.Id,
        TokenHash = YoPay.Application.Devices.PairingToken.Hash(pairingCode),
        ExpiresAt = DateTimeOffset.UtcNow + YoPay.Application.Devices.PairingToken.Lifetime,
    });

    await tokenDb.SaveChangesAsync();

    Console.WriteLine($"pairing code  {pairingCode}");
    Console.WriteLine($"wallet        {targetWallet.Number}");
    Console.WriteLine($"valid for     {YoPay.Application.Devices.PairingToken.Lifetime.TotalMinutes} minutes, one use");
    Console.WriteLine();
    Console.WriteLine($"  dotnet run --project tools/YoPay.DeviceSim -- pair {pairingCode}");
    return 0;
}

var merchantName = args.Length > 0 ? args[0] : "Demo Shop";
var walletNumber = args.Length > 1 ? args[1] : "01700000000";
var slug = merchantName.ToLowerInvariant().Replace(' ', '-');

var options = new DbContextOptionsBuilder<YoPayDbContext>()
    .UseNpgsql(connectionString)
    .Options;

await using var db = new YoPayDbContext(options);

var merchant = await db.Merchants.FirstOrDefaultAsync(m => m.Slug == slug);

if (merchant is null)
{
    merchant = new Merchant
    {
        Name = merchantName,
        Slug = slug,
        ContactEmail = $"{slug}@example.com",
    };

    db.Merchants.Add(merchant);
    Console.WriteLine($"merchant   created  {merchant.Name}  {merchant.Id}");
}
else
{
    Console.WriteLine($"merchant   existing {merchant.Name}  {merchant.Id}");
}

var wallet = await db.Wallets.FirstOrDefaultAsync(w =>
    w.MerchantId == merchant.Id && w.Number == walletNumber);

if (wallet is null)
{
    wallet = new Wallet
    {
        MerchantId = merchant.Id,
        Method = PaymentMethod.Bkash,
        Number = walletNumber,
        AccountType = WalletAccountType.PersonalRetail,
        Label = "Primary",
    };

    db.Wallets.Add(wallet);
    Console.WriteLine($"wallet     created  bKash {wallet.Number}  {wallet.Id}");
}
else
{
    Console.WriteLine($"wallet     existing bKash {wallet.Number}  {wallet.Id}");
}

// A fresh credential every run. The signing secret is shown once and never stored in
// readable form, so losing it means issuing another - which is the behaviour a merchant
// should see in production too.
var (keyId, apiKey, apiKeyHash) = ApiKey.Issue();
var hmacSecret = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

db.ApiCredentials.Add(new ApiCredential
{
    MerchantId = merchant.Id,
    KeyId = keyId,
    ApiKeyHash = apiKeyHash,
    HmacSecretEncrypted = protector.Protect(hmacSecret),
    Label = "seeded",
});

await db.SaveChangesAsync();

Console.WriteLine();
Console.WriteLine("credential created. The signing secret is shown once.");
Console.WriteLine();
Console.WriteLine($"  KeyId        {keyId}");
Console.WriteLine($"  HmacSecret   {hmacSecret}");
Console.WriteLine($"  ApiKey       {apiKey}");
Console.WriteLine();
Console.WriteLine("Set these for the PowerShell client:");
Console.WriteLine($"  $env:YOPAY_KEY_ID = '{keyId}'");
Console.WriteLine($"  $env:YOPAY_SECRET = '{hmacSecret}'");

return 0;
