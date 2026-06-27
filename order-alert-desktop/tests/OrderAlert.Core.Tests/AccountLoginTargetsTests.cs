using OrderAlert.Core.Chrome;
using OrderAlert.Core.Models;

namespace OrderAlert.Core.Tests;

public sealed class AccountLoginTargetsTests
{
    [Fact]
    public void Dianxiaomi_login_opens_all_four_fixed_pages()
    {
        var urls = AccountLoginTargets.For(PlatformKind.Dianxiaomi);

        Assert.Equal(4, urls.Count);
        Assert.Contains(urls, url => url.Contains("/paid?go=m100"));
        Assert.Contains(urls, url => url.Contains("/approved?go=m101"));
        Assert.Contains(
            urls,
            url => url.Contains("/processed/self_warehouse?go=m10201"));
        Assert.Contains(urls, url => url.Contains("/allocated/has?go=m10301"));
    }

    [Fact]
    public void Aliexpress_login_opens_the_order_management_page()
    {
        var url = Assert.Single(AccountLoginTargets.For(PlatformKind.AliExpress));

        Assert.Contains("order-manage/orderList", url);
    }
}
