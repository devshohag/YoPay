using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Security.Cryptography;
using YoPay.Application.Abstractions;
using YoPay.Application.Devices;
using YoPay.Application.Ingestion;
using YoPay.Application.Invoicing;
using YoPay.Application.Security;
using YoPay.Domain.Entities;
using YoPay.Domain.Enums;
using YoPay.Infrastructure.Persistence;

namespace YoPay.Api.Pages;

/// <summary>
/// Everything needed to exercise the system, on one page.
///
/// It exists because the alternative was four terminals, a signing script and a psql
/// prompt to answer the question "did that payment land". A tool nobody wants to use does
/// not get used, and a payment system nobody exercises is a payment system nobody trusts.
///
/// Development only, and the route is not mapped outside Development. There is no login
/// here: it reads and writes every merchant's data, so the day it is reachable from
/// anywhere but a laptop is the day it becomes the worst hole in the product.
///
/// Every POST answers with a redirect rather than with HTML (Post/Redirect/Get). Without
/// it, the browser remembers the POST: F5 sends the same "pay it" again, a second claim
/// and a second message are written, and the page that comes back is the one rendered
/// before the worker ran - so the button stays on screen for an invoice that is already
/// paid. Both of those read as backend bugs and neither of them is one. The message that
/// follows a POST travels in TempData, which survives exactly one redirect and then
/// disappears.
/// </summary>
public class DashboardModel(
    YoPayDbContext db,
    CreateInvoiceService invoices,
    ISecretProtector protector) : PageModel
{
    public IReadOnlyList<Merchant> Merchants { get; private set; } = [];
    public IReadOnlyList<InvoiceRow> Invoices { get; private set; } = [];
    public IReadOnlyList<DeviceRow> Devices { get; private set; } = [];
    public IReadOnlyList<EventRow> Events { get; private set; } = [];

    [TempData] public string? Notice { get; set; }
    [TempData] public string? PairingCode { get; set; }
    [TempData] public string? CheckoutUrl { get; set; }
    [TempData] public string? IssuedKeyId { get; set; }
    [TempData] public string? IssuedSecret { get; set; }

    [BindProperty]
    public decimal Amount { get; set; } = 500m;

    [BindProperty]
    public string? OrderRef { get; set; }

    /// <summary>True while the worker still has something to do, which is what the page
    /// uses to decide whether refreshing itself is worth the noise.</summary>
    public bool Working { get; private set; }

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct).ConfigureAwait(false);

    public async Task<IActionResult> OnPostCreateInvoiceAsync(CancellationToken ct)
    {
        var merchant = await db.Merchants.AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (merchant is null)
        {
            Notice = "No merchant exists. Run the seeder first.";
            return RedirectToPage();
        }

        var orderRef = string.IsNullOrWhiteSpace(OrderRef)
            ? $"ORD-{DateTimeOffset.UtcNow:yyMMddHHmmss}"
            : OrderRef.Trim();

        var result = await invoices.CreateAsync(
            new CreateInvoiceCommand
            {
                MerchantId = merchant.Id,
                OrderRef = orderRef,
                Amount = Amount,
                Method = PaymentMethod.Bkash,
            },
            ct: ct).ConfigureAwait(false);

        if (result.Invoice is { } invoice)
        {
            var baseUrl = HttpContext.RequestServices
                .GetRequiredService<IConfiguration>()["Checkout:BaseUrl"]?.TrimEnd('/')
                ?? "http://localhost:5082";

            CheckoutUrl = $"{baseUrl}/pay/{invoice.Id}";
            Notice = $"{result.Outcome}: {orderRef} for {invoice.ChargedAmount:N2}";
        }
        else
        {
            Notice = $"{result.Outcome}: {result.Reason}";
        }

        return RedirectToPage();
    }

    /// <summary>
    /// Creates the merchant and wallet a fresh database needs.
    ///
    /// Here rather than only in the seeder because "run a console tool first" is a step
    /// that gets forgotten every time the database is dropped, and the failure it produces
    /// - a dashboard where the buttons quietly do nothing - explains itself to nobody.
    /// </summary>
    public async Task<IActionResult> OnPostSeedAsync(CancellationToken ct)
    {
        if (await db.Merchants.AnyAsync(ct).ConfigureAwait(false))
        {
            Notice = "A merchant already exists.";
            return RedirectToPage();
        }

        var merchant = new Merchant
        {
            Name = "Demo Shop",
            Slug = "demo-shop",
            ContactEmail = "demo@example.com",
        };

        db.Merchants.Add(merchant);

        db.Wallets.Add(new Wallet
        {
            MerchantId = merchant.Id,
            Method = PaymentMethod.Bkash,
            Number = "01700000000",
            AccountType = WalletAccountType.PersonalRetail,
            Label = "Primary",
        });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        Notice = "Created Demo Shop with a bKash wallet on 01700000000.";
        return RedirectToPage();
    }

    /// <summary>
    /// Issues an API credential for testing an SDK or the signing script. The secret is
    /// shown once and stored encrypted, which is the behaviour a merchant should see in
    /// production too - there is no screen anywhere that can show it again.
    /// </summary>
    public async Task<IActionResult> OnPostIssueKeyAsync(CancellationToken ct)
    {
        var merchant = await db.Merchants.AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (merchant is null)
        {
            Notice = "Seed a merchant first.";
            return RedirectToPage();
        }

        var (keyId, _, apiKeyHash) = ApiKey.Issue();
        var secret = Convert.ToBase64String(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

        db.ApiCredentials.Add(new ApiCredential
        {
            MerchantId = merchant.Id,
            KeyId = keyId,
            ApiKeyHash = apiKeyHash,
            HmacSecretEncrypted = protector.Protect(secret),
            Label = "dashboard",
        });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        IssuedKeyId = keyId;
        IssuedSecret = secret;
        Notice = "Credential issued. The signing secret is shown once.";

        return RedirectToPage();
    }

    /// <summary>
    /// Plays out a whole payment without a phone: the customer types a transaction id on
    /// the checkout page, and a moment later the message arrives from the handset.
    ///
    /// It writes the same two rows those two events would write and then gets out of the
    /// way - the real parser reads the message, the real matcher decides, the real state
    /// machine moves the invoice. Nothing here marks anything paid. A demo that shortcuts
    /// to the answer proves only that the demo works.
    ///
    /// An invoice that has already left AwaitingPayment is refused rather than paid twice,
    /// because the button can still be on screen in a tab opened a minute ago.
    /// </summary>
    public async Task<IActionResult> OnPostSimulateAsync(Guid invoiceId, CancellationToken ct)
    {
        var invoice = await db.Invoices
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
            .ConfigureAwait(false);

        if (invoice is null)
        {
            Notice = "Invoice not found.";
            return RedirectToPage();
        }

        if (invoice.Status != InvoiceStatus.AwaitingPayment)
        {
            Notice = $"{invoice.OrderRef} is already {invoice.Status}. Nothing sent.";
            return RedirectToPage();
        }

        if (DateTimeOffset.UtcNow > invoice.GraceUntil)
        {
            // The button is hidden for these, but a stale tab still has it. Sending the
            // payment anyway would write a message the matcher is bound to refuse, and
            // then the queue fills with failures the console itself manufactured.
            Notice = $"{invoice.OrderRef} closed its window at " +
                     $"{invoice.GraceUntil.ToLocalTime():HH:mm}. Create a new invoice.";
            return RedirectToPage();
        }

        var device = await db.Devices
            .FirstOrDefaultAsync(d => d.WalletId == invoice.WalletId && d.Model == SimulatorModel, ct)
            .ConfigureAwait(false);

        if (device is null)
        {
            device = new Device
            {
                MerchantId = invoice.MerchantId,
                WalletId = invoice.WalletId,
                Fingerprint = Guid.CreateVersion7().ToString("N"),
                Model = SimulatorModel,
                AppVersion = "dashboard",
                Priority = 9,
            };

            db.Devices.Add(device);
        }

        var trxId = NewTrxId();
        var now = DateTimeOffset.UtcNow;

        // What the customer typed into the checkout page.
        db.PaymentClaims.Add(new PaymentClaim
        {
            InvoiceId = invoice.Id,
            SubmittedText = trxId,
            NormalisedTrxId = trxId,
            SubmittedAt = now,
            ClientIp = "dashboard",
            State = ClaimState.Pending,
        });

        // What the handset saw, in bKash's own wording, with the provider timestamp in
        // Dhaka local time - which is what the parser expects and what the matcher trusts.
        var dhakaTime = now.ToOffset(TimeSpan.FromHours(6));

        var body =
            $"You have received Tk {invoice.ChargedAmount.ToString("N2", CultureInfo.InvariantCulture)} " +
            "from 01711111111. Fee Tk 0.00. Balance Tk 9,999.00. " +
            $"TrxID {trxId} at {dhakaTime:dd/MM/yyyy HH:mm}";

        db.RawEvents.Add(new RawEvent
        {
            MerchantId = invoice.MerchantId,
            DeviceId = device.Id,
            Source = EventSource.Notification,
            SenderId = "bKash",
            Body = body,
            DeviceReceivedAt = now,
            ServerReceivedAt = now,
            DedupeHash = DedupeHash.Compute(device.Id, "bKash", body, now),
            State = RawEventState.Received,
        });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        Notice = $"Sent a payment of {invoice.ChargedAmount:N2} with TrxID {trxId}. " +
                 "The worker picks it up within a couple of seconds.";

        return RedirectToPage();
    }

    private static string NewTrxId()
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var chars = new char[10];

        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        }

        return new string(chars);
    }

    private const string SimulatorModel = "Dashboard simulator";

    public async Task<IActionResult> OnPostPairingCodeAsync(CancellationToken ct)
    {
        var wallet = await db.Wallets
            .AsNoTracking()
            .OrderBy(w => w.CreatedAt)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (wallet is null)
        {
            Notice = "No wallet exists. Run the seeder first.";
            return RedirectToPage();
        }

        var code = PairingToken.New();

        db.DevicePairingTokens.Add(new DevicePairingToken
        {
            MerchantId = wallet.MerchantId,
            WalletId = wallet.Id,
            TokenHash = PairingToken.Hash(code),
            ExpiresAt = DateTimeOffset.UtcNow + PairingToken.Lifetime,
        });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        PairingCode = code;
        Notice = $"Pairing code valid for {PairingToken.Lifetime.TotalMinutes} minutes, one use.";

        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        Merchants = await db.Merchants.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);

        Invoices = await db.Invoices
            .AsNoTracking()
            .OrderByDescending(i => i.CreatedAt)
            .Take(15)
            .Select(i => new InvoiceRow(
                i.Id, i.OrderRef, i.Amount, i.ChargedAmount, i.Status, i.CreatedAt, i.PaidAt,
                i.GraceUntil))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        Devices = await db.Devices
            .AsNoTracking()
            .Select(d => new DeviceRow(
                d.Id, d.Model, d.AppVersion, d.LastHeartbeatAt, d.PermissionState, d.BatteryPercent))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // The two states worth looking at are the ones that mean something went wrong:
        // a message nobody could read, and money nothing was waiting for.
        Events = await db.RawEvents
            .AsNoTracking()
            .OrderByDescending(e => e.ServerReceivedAt)
            .Take(15)
            .Select(e => new EventRow(
                e.Id, e.SenderId, e.Body, e.State, e.DeviceReceivedAt, e.FailureReason))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // Only while something is genuinely in flight. A row that has been sitting in
        // Claimed for a minute is not in flight, it is stuck, and a page that keeps
        // reloading itself over it hides that instead of showing it.
        var fresh = DateTimeOffset.UtcNow.AddSeconds(-60);

        Working = Events.Any(e =>
            (e.State is RawEventState.Received or RawEventState.Claimed) &&
            e.DeviceReceivedAt > fresh);
    }

    public sealed record InvoiceRow(
        Guid Id, string OrderRef, decimal Amount, decimal ChargedAmount,
        InvoiceStatus Status, DateTimeOffset CreatedAt, DateTimeOffset? PaidAt,
        DateTimeOffset GraceUntil)
    {
        /// <summary>
        /// A payment that arrives after this is refused by the matcher, and until the
        /// dashboard showed it there was no way to see that from the outside - the invoice
        /// simply stayed unpaid while perfectly good messages kept arriving.
        /// </summary>
        public bool WindowClosed => DateTimeOffset.UtcNow > GraceUntil;
    }

    public sealed record DeviceRow(
        Guid Id, string? Model, string? AppVersion, DateTimeOffset? LastHeartbeatAt,
        DevicePermissionState PermissionState, int? BatteryPercent);

    public sealed record EventRow(
        Guid Id, string SenderId, string Body, RawEventState State,
        DateTimeOffset DeviceReceivedAt, string? FailureReason);
}
