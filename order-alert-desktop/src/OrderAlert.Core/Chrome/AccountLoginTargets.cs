using OrderAlert.Core.Models;

namespace OrderAlert.Core.Chrome;

public static class AccountLoginTargets
{
    private static readonly string[] Dianxiaomi =
    [
        "https://www.dianxiaomi.com/web/order/paid?go=m100",
        "https://www.dianxiaomi.com/web/order/approved?go=m101",
        "https://www.dianxiaomi.com/web/order/processed/self_warehouse?go=m10201",
        "https://www.dianxiaomi.com/web/order/allocated/has?go=m10301"
    ];

    private static readonly string[] AliExpress =
    [
        "https://csp.aliexpress.com/m_apps/order-manage/orderList?channelId=244176"
    ];

    public static IReadOnlyList<string> For(PlatformKind platform) =>
        platform == PlatformKind.Dianxiaomi ? Dianxiaomi : AliExpress;
}
