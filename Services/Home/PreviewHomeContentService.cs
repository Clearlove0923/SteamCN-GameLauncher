using SteamCNGameLauncher.Models.Home;

namespace SteamCNGameLauncher.Services.Home;

/// <summary>
/// Python 传输接入前使用的首页预览服务。页面仍通过 IHomeContentService 消费统一模型，
/// 后续替换服务实现即可接入真实 Banner 和资讯，不需要改动首页控件。
/// </summary>
public sealed class PreviewHomeContentService : IHomeContentService
{
    public const string ProviderId = "preview";

    public Task<HomeContentResult> GetAsync(
        HomeContentRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var content = new HomeContent
        {
            Banners =
            [
                new HomeBanner
                {
                    Id = $"{request.GameId}-preview",
                    Title = "首页 Banner 将由 Python 模块提供"
                }
            ],
            News =
            [
                new HomeNewsItem { Id = "preview-activity-1", Category = "activity", Title = "活动内容等待接入（一）" },
                new HomeNewsItem { Id = "preview-activity-2", Category = "activity", Title = "活动内容等待接入（二）" },
                new HomeNewsItem { Id = "preview-activity-3", Category = "activity", Title = "活动内容等待接入（三）" },
                new HomeNewsItem { Id = "preview-activity-4", Category = "activity", Title = "更多活动可使用滚轮查看" },
                new HomeNewsItem { Id = "preview-announcement", Category = "announcement", Title = "公告内容等待接入" },
                new HomeNewsItem { Id = "preview-information", Category = "information", Title = "资讯内容等待接入" },
                new HomeNewsItem { Id = "preview-guide", Category = "攻略", Title = "自定义分类名称由 Python 数据提供" }
            ]
        };
        return Task.FromResult(new HomeContentResult(content));
    }
}
