using System.Globalization;
using System.Net;
using System.Text;

/// <summary>
/// The shop's two pages, written out rather than templated. Nothing here is the point of
/// the sample - the point is in Program.cs - and a Razor project would bury it.
/// </summary>
internal static class Pages
{
    public static string Home(IReadOnlyCollection<Order> orders)
    {
        var rows = new StringBuilder();

        foreach (var order in orders)
        {
            rows.Append(CultureInfo.InvariantCulture, $"""
                <tr>
                  <td>{Encode(order.Reference)}</td>
                  <td>{order.Total.ToString("N2", CultureInfo.InvariantCulture)}</td>
                  <td>{order.Charged?.ToString("N2", CultureInfo.InvariantCulture) ?? "&mdash;"}</td>
                  <td><b class="{Encode(order.State)}">{Encode(order.State)}</b></td>
                  <td class="mono">{Encode(order.TrxId ?? "—")}</td>
                </tr>
                """);
        }

        if (orders.Count == 0)
        {
            rows.Append("""<tr><td colspan="5" class="muted">no orders yet</td></tr>""");
        }

        return $$"""
            <!DOCTYPE html>
            <html lang="en"><head><meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1" />
            <title>Demo Shop</title>
            <style>
              body { font-family: system-ui, sans-serif; margin: 0; padding: 24px;
                     background: #f6f8fa; color: #16202e; font-size: 14px; }
              .wrap { max-width: 760px; margin: 0 auto; }
              .card { background: #fff; border: 1px solid #e2e8f0; border-radius: 8px;
                      padding: 16px; margin-bottom: 14px; }
              h1 { font-size: 20px; margin: 0 0 4px; }
              h2 { font-size: 14px; margin: 0 0 10px; color: #0f766e; }
              table { width: 100%; border-collapse: collapse; font-size: 13px; }
              th { text-align: left; color: #64748b; font-weight: 600; padding: 4px 8px 6px;
                   border-bottom: 1px solid #e2e8f0; }
              td { padding: 6px 8px; border-bottom: 1px solid #f1f5f9; }
              input, button { font-size: 14px; padding: 8px 10px; border: 1px solid #cbd5e1;
                              border-radius: 6px; }
              button { background: #0d9488; color: #fff; border: 0; font-weight: 600; cursor: pointer; }
              .mono { font-family: ui-monospace, monospace; font-size: 12px; }
              .muted { color: #94a3b8; }
              .Fulfilled { color: #047857; } .AwaitingPayment { color: #0369a1; }
              .note { font-size: 12px; color: #64748b; margin-top: 8px; }
            </style></head><body><div class="wrap">
              <h1>Demo Shop</h1>
              <p class="muted">A merchant's own application, using the YoPay SDK and nothing else.</p>

              <div class="card">
                <h2>Buy something</h2>
                <form method="post" action="/buy">
                  <input name="amount" value="500" style="width:110px" />
                  <input name="phone" value="01711111111" style="width:160px" />
                  <button type="submit">Pay with bKash</button>
                </form>
                <div class="note">
                  Sends you to the YoPay checkout. The order below moves to Fulfilled only
                  when the webhook arrives <em>and</em> the server confirms the payment.
                </div>
              </div>

              <div class="card">
                <h2>Orders</h2>
                <table>
                  <tr><th>Reference</th><th>Total</th><th>Charged</th><th>State</th><th>Transaction</th></tr>
                  {{rows}}
                </table>
              </div>
            </div></body></html>
            """;
    }

    public static string Trouble(string message) => $"""
        <!DOCTYPE html><html lang="en"><head><meta charset="utf-8" /><title>Sorry</title>
        <style>body{{font-family:system-ui,sans-serif;margin:0;padding:40px;color:#16202e}}</style>
        </head><body>
          <h1>That did not work</h1>
          <p>{Encode(message)}</p>
          <p><a href="/">Back to the shop</a></p>
        </body></html>
        """;

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
